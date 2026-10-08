namespace Sasayaki.Core;

public readonly record struct KeyTransition(ushort Key, bool Down);
public sealed record KeyRoute(bool Suppress, string? Gesture, KeyTransition[] Replay);

/// <summary>Tracks physical keys separately from keys hidden from the foreground application.</summary>
public sealed class KeyboardRouter(HotkeySettings? shortcut = null)
{
    private readonly HotkeySettings shortcut = shortcut ?? new();
    private readonly HashSet<ushort> physical = [], suppressed = [];
    private bool chord;
    public void Reset() { physical.Clear(); suppressed.Clear(); chord = false; }
    public KeyRoute Route(ushort key, bool down, bool injected = false)
    {
        if (injected) return new(false, null, []);
        var repeat = down && !physical.Add(key);
        if (!down) physical.Remove(key);
        var nextChord = shortcut.Matches(physical.Where(shortcut.ContainsKey));
        var ctrlWin = shortcut.ContainsKey(key);
        if (down && !repeat && nextChord && !chord && shortcut.Matches(physical))
        {
            chord = true; suppressed.Add(key);
            return new(true, "down", [new(0xE8, true), new(0xE8, false)]);
        }
        string? gesture = null;
        if (down && !repeat && !ctrlWin && chord)
        {
            gesture = key == 0x1B ? "escape" : "other";
            if (key != 0x1B && suppressed.Count > 0)
            {
                var replay = suppressed.Select(k => new KeyTransition(k, true)).Append(new(key, true)).ToArray();
                suppressed.Clear(); return new(true, gesture, replay);
            }
        }
        else if (down && key == 0x1B && !repeat) gesture = "escape";
        if (chord && !nextChord) { chord = false; gesture = "up"; }
        if (!down && suppressed.Remove(key)) return new(true, gesture, []);
        return new(down && suppressed.Contains(key), gesture, []);
    }
}
