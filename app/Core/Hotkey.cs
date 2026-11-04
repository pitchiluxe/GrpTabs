namespace GrpTabs.Core;

public enum HotkeyAction { Next, Previous, Detach, Pause }

/// <summary>A global hotkey such as "Ctrl+Alt+PageDown": parsing, display and the RegisterHotKey values.</summary>
public readonly record struct Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, Keys Key)
{
    public static readonly IReadOnlyDictionary<HotkeyAction, string> Labels = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.Next] = "Switch to the next tab in the group",
        [HotkeyAction.Previous] = "Switch to the previous tab in the group",
        [HotkeyAction.Detach] = "Take the active window out of its group",
        [HotkeyAction.Pause] = "Pause or resume GrpTabs",
    };

    private static readonly Dictionary<string, Keys> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PageDown"] = Keys.PageDown, ["PgDn"] = Keys.PageDown, ["PageUp"] = Keys.PageUp, ["PgUp"] = Keys.PageUp,
        ["Left"] = Keys.Left, ["Right"] = Keys.Right, ["Up"] = Keys.Up, ["Down"] = Keys.Down,
        ["Tab"] = Keys.Tab, ["Space"] = Keys.Space, ["Home"] = Keys.Home, ["End"] = Keys.End,
        ["Insert"] = Keys.Insert, ["Delete"] = Keys.Delete, ["`"] = Keys.Oemtilde,
    };

    /// <summary>"Ctrl+Alt+D" to a hotkey; null without a modifier, without a key, or with two keys.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        bool ctrl = false, alt = false, shift = false, win = false;
        Keys? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                default:
                    if (key is not null) return null;
                    key = KeyOf(raw);
                    if (key is null) return null;
                    break;
            }
        }
        if (key is null || !(ctrl || alt || win)) return null;
        return new Hotkey(ctrl, alt, shift, win, key.Value);
    }

    private static Keys? KeyOf(string name)
    {
        if (Names.TryGetValue(name, out var k)) return k;
        if (name.Length == 1 && char.IsLetterOrDigit(name[0])) return char.IsDigit(name[0]) ? Keys.D0 + (name[0] - '0') : Keys.A + (char.ToUpperInvariant(name[0]) - 'A');
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24) return Keys.F1 + (f - 1);
        return null;
    }

    public static string KeyName(Keys k) => k switch
    {
        Keys.PageDown => "PageDown", Keys.PageUp => "PageUp", Keys.Oemtilde => "`",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        _ => k.ToString(),
    };

    /// <summary>From a key press in the hotkey box; null for a lone modifier or no Ctrl/Alt/Win.</summary>
    public static Hotkey? FromKeys(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin or Keys.None) return null;
        bool ctrl = keyData.HasFlag(Keys.Control), alt = keyData.HasFlag(Keys.Alt), shift = keyData.HasFlag(Keys.Shift);
        if (!(ctrl || alt)) return null;
        return new Hotkey(ctrl, alt, shift, false, key);
    }

    public override string ToString() => string.Join("+", new[] { Ctrl ? "Ctrl" : null, Win ? "Win" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, KeyName(Key) }.Where(s => s is not null));

    public uint Modifiers => (Ctrl ? Native.MOD_CONTROL : 0) | (Alt ? Native.MOD_ALT : 0) | (Shift ? Native.MOD_SHIFT : 0) | (Win ? Native.MOD_WIN : 0) | Native.MOD_NOREPEAT;

    /// <summary>Pairs of actions that share a hotkey.</summary>
    public static List<(HotkeyAction, HotkeyAction)> Clashes(IReadOnlyDictionary<HotkeyAction, string> map)
    {
        var parsed = map.Select(kv => (kv.Key, Hk: Parse(kv.Value))).Where(x => x.Hk is not null).ToList();
        var outList = new List<(HotkeyAction, HotkeyAction)>();
        for (int i = 0; i < parsed.Count; i++)
            for (int j = i + 1; j < parsed.Count; j++)
                if (parsed[i].Hk!.Value.ToString() == parsed[j].Hk!.Value.ToString()) outList.Add((parsed[i].Key, parsed[j].Key));
        return outList;
    }
}
