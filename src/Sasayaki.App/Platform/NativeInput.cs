using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sasayaki.Core;

namespace Sasayaki.App.Platform;

internal static class NativeInput
{
    internal static readonly nuint Tag = 0x53415341;
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    // INPUT's union is 32 bytes on x64; mouse input determines its size.
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    internal static Input Key(ushort key, bool up, bool unicode = false) => new()
    { Type = 1, Data = new Union { Keyboard = new Keyboard { Key = unicode ? (ushort)0 : key, Scan = unicode ? key : (ushort)0, Flags = (up ? 2u : 0) | (unicode ? 4u : key is 0x5B or 0x5C or 0xA3 or 0xA5 ? 1u : 0), Extra = Tag } } };
    internal static bool Submit(Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    internal static (nint Window, nint Focus) Target()
    {
        var window = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(window, out var process);
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        if (window == 0 || process == Environment.ProcessId || !GetGUIThreadInfo(thread, ref info) || info.Focus == 0) return default;
        return (window, info.Focus);
    }
}

public sealed class TextInjector
{
    public async Task InsertAsync(string text, CancellationToken cancellationToken)
    {
        if (text.Length > 16000 || text.Any(char.IsControl))
            throw new ServiceException("Text contains unsupported control characters or is too long. The result is retained.");
        var target = NativeInput.Target();
        if (target == default) throw new ServiceException("Focus a text field in another application, then arm the retained text.");
        var wait = Stopwatch.StartNew();
        while (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => NativeInput.GetAsyncKeyState(k) < 0))
        {
            if (wait.ElapsedMilliseconds > 1500) throw new ServiceException("Modifiers are still held. The result is retained.");
            await Task.Delay(10, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (NativeInput.Target() != target) throw new ServiceException("Focus changed while preparing insertion. The result is retained.");
        var inputs = text.SelectMany(c => new[] { NativeInput.Key(c, false, true), NativeInput.Key(c, true, true) }).ToArray();
        if (!NativeInput.Submit(inputs))
            throw new ServiceException("Windows blocked or only partly submitted the text. Check the destination before deliberately retrying; retrying may duplicate text.");
    }
}

/// <summary>Owns its hook and message pump. The callback performs only key bookkeeping and input replay.</summary>
public sealed class KeyboardHook : IDisposable
{
    private delegate nint HookProc(int code, nuint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct HookData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint w, nint l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    private readonly Thread thread;
    private readonly HookProc callback;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly KeyboardRouter router = new();
    private nint hook;
    private uint threadId;
    public event Action<string, long>? Event;
    public KeyboardHook()
    {
        callback = OnKey;
        thread = new Thread(Run) { IsBackground = true, Name = "Sasayaki keyboard" };
        thread.Start(); ready.Task.GetAwaiter().GetResult();
    }
    private void Run()
    {
        threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0);
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0) { ready.TrySetException(new Win32Exception(Marshal.GetLastWin32Error())); return; }
        ready.TrySetResult();
        try
        {
            while (GetMessage(out var message, 0, 0, 0) > 0)
                if (message.Id == 0x8001) router.Reset();
        }
        finally { UnhookWindowsHookEx(hook); }
    }
    public void Reset() => PostThreadMessage(threadId, 0x8001, 0, 0);
    private nint OnKey(int code, nuint message, nint pointer)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, pointer);
        var data = Marshal.PtrToStructure<HookData>(pointer);
        var now = Environment.TickCount64;
        var route = router.Route((ushort)data.Key, message is 0x100 or 0x104, (data.Flags & 0x10) != 0);
        if (route.Replay.Length > 0 && !NativeInput.Submit(route.Replay.Select(k => NativeInput.Key(k.Key, !k.Down)).ToArray()))
            Event?.Invoke("input-error", now);
        if (route.Gesture != null) Event?.Invoke(route.Gesture, now);
        if (route.Suppress) return 1;
        return CallNextHookEx(hook, code, message, pointer);
    }
    public void Dispose() { PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(TimeSpan.FromSeconds(2)); }
}
