namespace Sasayaki.Core;

public sealed record HotkeySettings
{
    public uint Modifiers { get; init; } = 10;
    public ushort Key { get; init; }
    public bool IsDefault => Modifiers == 10 && Key == 0;
    public string DisplayName => IsDefault ? "Ctrl+Win" :
        ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") +
        ((Modifiers & 4) != 0 ? "Shift+" : "") + (Key >= 0x70 ? $"F{Key - 0x6F}" : ((char)Key).ToString());

    public void Validate()
    {
        if (IsDefault) return;
        if (Modifiers is not (3 or 5 or 6 or 7) || !(Key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7A))
            throw new ConfigurationException("Choose Ctrl+Win, or at least two of Ctrl/Alt/Shift plus a letter, number, or F1–F11.");
    }

    public bool ContainsKey(ushort key) => key == Key || (ModifierFor(key) & Modifiers) != 0;
    public bool Matches(IEnumerable<ushort> keys)
    {
        uint modifiers = 0;
        var found = Key == 0;
        foreach (var key in keys)
        {
            var modifier = ModifierFor(key);
            if (modifier != 0) modifiers |= modifier;
            else if (key == Key) found = true;
            else return false;
        }
        return found && modifiers == Modifiers;
    }
    private static uint ModifierFor(ushort key) => key switch
    { 0x10 or 0xA0 or 0xA1 => 4, 0x11 or 0xA2 or 0xA3 => 2, 0x12 or 0xA4 or 0xA5 => 1, 0x5B or 0x5C => 8, _ => 0 };
}
