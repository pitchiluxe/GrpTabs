using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace GrpTabs.Core;

public enum TabsWhen { Always, Hover, Never }
public enum GrpTheme { Auto, Light, Dark }

/// <summary>Everything the settings window changes, saved as JSON in %AppData%\GrpTabs.</summary>
public sealed class Settings
{
    public bool TrayIcon { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public TabsWhen GroupedTabs { get; set; } = TabsWhen.Always;
    public TabsWhen SingleTabs { get; set; } = TabsWhen.Hover;
    /// <summary>Opacity of tabs when the pointer is elsewhere, 30-100.</summary>
    public int IdleOpacity { get; set; } = 75;
    /// <summary>Widest a tab gets, in pixels at 100% scale, 90-260.</summary>
    public int TabWidth { get; set; } = 190;
    public bool ShowIcons { get; set; } = true;
    public GrpTheme Theme { get; set; } = GrpTheme.Auto;
    public bool HideWhenMaximized { get; set; }
    public bool MiddleClickCloses { get; set; } = true;
    public bool DoubleClickMaximizes { get; set; } = true;
    public bool DragOutDetaches { get; set; } = true;
    /// <summary>Program file names (e.g. "Taskmgr.exe") whose windows never get tabs.</summary>
    public List<string> Exceptions { get; set; } = new() { "Taskmgr.exe", "SnippingTool.exe", "ScreenClippingHost.exe" };
    /// <summary>Program file names whose new windows join an open window of the same program.</summary>
    public List<string> AutoGroup { get; set; } = new();
    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = DefaultHotkeys();

    public static Dictionary<HotkeyAction, string> DefaultHotkeys() => new()
    {
        [HotkeyAction.Next] = "Ctrl+Alt+PageDown",
        [HotkeyAction.Previous] = "Ctrl+Alt+PageUp",
        [HotkeyAction.Detach] = "Ctrl+Alt+D",
        [HotkeyAction.Pause] = "Ctrl+Alt+G",
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpTabs");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Repairs anything out of range or missing, so a hand-edited file cannot break the app.</summary>
    public Settings Validated()
    {
        IdleOpacity = Math.Clamp(IdleOpacity, 30, 100);
        TabWidth = Math.Clamp(TabWidth, 90, 260);
        Exceptions = Clean(Exceptions);
        AutoGroup = Clean(AutoGroup);
        var defaults = DefaultHotkeys();
        Hotkeys ??= new();
        foreach (var a in Enum.GetValues<HotkeyAction>())
        {
            if (!Hotkeys.TryGetValue(a, out var v) || (v != "" && Hotkey.Parse(v) is null)) Hotkeys[a] = defaults[a];
        }
        return this;
    }

    private static List<string> Clean(List<string>? list) =>
        (list ?? new()).Where(s => !string.IsNullOrWhiteSpace(s) && s.Length < 120).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static Settings Parse(string json)
    {
        try { return (JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings()).Validated(); }
        catch (JsonException) { return new Settings(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Settings Load()
    {
        try { return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new Settings(); }
        catch (IOException) { return new Settings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, FilePath, overwrite: true);
    }

    public Settings Clone() => Parse(ToJson());

    public bool IsException(string exe) => Exceptions.Contains(exe, StringComparer.OrdinalIgnoreCase);
    public bool IsAutoGroup(string exe) => AutoGroup.Contains(exe, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------ start with Windows

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>"Start with Windows" is the GrpTabs entry under HKCU\...\Run (also shown in Task Manager > Startup apps).</summary>
    public static bool StartsWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("GrpTabs") is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("GrpTabs", $"\"{Environment.ProcessPath}\" --startup");
            else k.DeleteValue("GrpTabs", throwOnMissingValue: false);
        }
    }
}
