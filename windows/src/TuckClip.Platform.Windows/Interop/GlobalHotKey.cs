namespace TuckClip.Platform.Windows.Interop;

public readonly record struct GlobalHotKey(uint VirtualKey, HotKeyModifiers Modifiers)
{
    private const HotKeyModifiers UserModifiers =
        HotKeyModifiers.Alt |
        HotKeyModifiers.Control |
        HotKeyModifiers.Shift |
        HotKeyModifiers.Windows;

    public static GlobalHotKey Default { get; } = new(
        0x56,
        HotKeyModifiers.Control | HotKeyModifiers.Alt);

    public HotKeyModifiers RegistrationModifiers => Modifiers | HotKeyModifiers.NoRepeat;

    public GlobalHotKey Validate()
    {
        if ((Modifiers & ~UserModifiers) != 0 || (Modifiers & UserModifiers) == 0)
        {
            throw new ArgumentException(
                "快捷键必须包含 Ctrl、Alt、Shift 或 Win 中的至少一个修饰键。",
                nameof(Modifiers));
        }

        if (VirtualKey is 0 or > 0xFE || IsModifierOnlyKey(VirtualKey))
        {
            throw new ArgumentException("快捷键还需要一个非修饰键。", nameof(VirtualKey));
        }

        return this with { Modifiers = Modifiers & UserModifiers };
    }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotKeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }
            if (Modifiers.HasFlag(HotKeyModifiers.Alt))
            {
                parts.Add("Alt");
            }
            if (Modifiers.HasFlag(HotKeyModifiers.Shift))
            {
                parts.Add("Shift");
            }
            if (Modifiers.HasFlag(HotKeyModifiers.Windows))
            {
                parts.Add("Win");
            }
            parts.Add(GetKeyName(VirtualKey));
            return string.Join("+", parts);
        }
    }

    private static bool IsModifierOnlyKey(uint virtualKey) => virtualKey is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string GetKeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }
        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }
        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"Num {virtualKey - 0x60}";
        }

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            0xBA => ";",
            0xBB => "+",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK 0x{virtualKey:X2}",
        };
    }
}
