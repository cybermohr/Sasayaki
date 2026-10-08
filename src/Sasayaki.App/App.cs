using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Sasayaki.App.Platform;
using Sasayaki.Core;
using Forms = System.Windows.Forms;

namespace Sasayaki.App;

public sealed class App : Application
{
    private readonly SettingsStore store = new();
    private AppSettings settings = new();
    private readonly HotkeyMachine gesture = new();
    private OverlayWindow overlay = null!;
    private DictationCoordinator coordinator = null!;
    private KeyboardHook? hook;
    private Forms.NotifyIcon? tray;
    private SettingsWindow? settingsWindow;
    private RecoveryWindow? recoveryWindow;
    private DispatcherTimer timer = null!;
    private long hideAt;
    private bool quitting;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--live-test") return LiveAcceptance.RunAsync(args[1], args[2]).GetAwaiter().GetResult();
        using var mutex = new Mutex(true, "Local\\Sasayaki.Desktop", out var first);
        if (!first) return 3;
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        return app.Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { settings = store.Load(); }
        catch { MessageBox.Show("Saved settings could not be read. Re-enter your Azure settings.", "Sasayaki"); }
        overlay = new();
        coordinator = new(Dispatcher, () => settings);
        gesture.Action += coordinator.Handle;
        coordinator.Status += text => { hideAt = Environment.TickCount64 + 5000; overlay.SetStatus(text); };
        coordinator.Level += overlay.SetLevel;
        coordinator.Completed += () => gesture.Complete();
        coordinator.Recovery += () =>
        {
            // Errors remain in the overlay. Open the recovery window only by a deliberate tray action.
            hideAt = long.MaxValue;
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("Review / retry retained text", null, (_, _) => Dispatcher.Invoke(OpenRecovery));
        menu.Items.Add("Discard retained text", null, (_, _) => Dispatcher.Invoke(() => coordinator.Discard()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, async (_, _) => await QuitAsync());
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Sasayaki · Ctrl+Win dictation", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        try
        {
            hook = new();
            hook.Event += (kind, time) => Dispatcher.BeginInvoke(() =>
            {
                switch (kind)
                {
                    case "down": gesture.Down(time); break;
                    case "up": gesture.Up(time); break;
                    case "escape":
                        if (coordinator.Armed && !coordinator.HasSession) { coordinator.Cancel(); gesture.Complete(); }
                        else gesture.Cancel(time);
                        break;
                    case "other": gesture.AbortCandidate(time); break;
                    case "input-error":
                        coordinator.Cancel(); gesture.Complete();
                        overlay.SetStatus("Windows blocked shortcut replay. Release all modifiers before continuing.");
                        break;
                }
            });
        }
        catch { MessageBox.Show("Windows could not install the keyboard hook. Quit and restart Sasayaki.", "Sasayaki"); }
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Normal, (_, _) =>
        {
            gesture.Tick(Environment.TickCount64); coordinator.Tick();
            if (!coordinator.HasSession && !coordinator.Armed && Environment.TickCount64 > hideAt) overlay.Hide();
        }, Dispatcher);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerMode;
        if (e.Args.Contains("--smoke-test"))
        {
            overlay.SetStatus("Startup smoke test");
            Dispatcher.BeginInvoke(async () => { await Task.Delay(750); await QuitAsync(); });
        }
        else if (string.IsNullOrEmpty(settings.SpeechKey) || !e.Args.Contains("--background")) OpenSettings();
    }
    private void OpenSettings()
    {
        if (coordinator.HasSession) return;
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        settingsWindow = new(settings, value => { store.Save(value); settings = value; });
        settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show();
    }
    private void OpenRecovery()
    {
        if (coordinator.HasSession) return;
        if (recoveryWindow != null) { recoveryWindow.Activate(); return; }
        recoveryWindow = new(coordinator.RetainedText, coordinator.Arm, coordinator.Discard);
        recoveryWindow.Closed += (_, _) => recoveryWindow = null; recoveryWindow.Show();
    }
    private void ResetSession() { coordinator.Cancel(); gesture.Complete(); hook?.Reset(); }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock) Dispatcher.BeginInvoke(ResetSession); }
    private void OnPowerMode(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode is PowerModes.Suspend or PowerModes.Resume) Dispatcher.BeginInvoke(ResetSession); }
    private async Task QuitAsync()
    {
        if (quitting) return; quitting = true;
        timer.Stop(); hook?.Dispose();
        SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.PowerModeChanged -= OnPowerMode;
        await coordinator.DisposeAsync();
        tray?.Dispose(); Shutdown();
    }
}
