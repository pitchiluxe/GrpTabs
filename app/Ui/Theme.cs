using GrpTabs.Core;
using Microsoft.Win32;

namespace GrpTabs.Ui;

/// <summary>GrpTabs colours (the same indigo as the website and the icon).</summary>
internal sealed record Palette(Color Tab, Color TabInk, Color Line, Color On, Color OnInk, Color Hover)
{
    public static readonly Color Indigo = Color.FromArgb(79, 70, 229);
    public static readonly Color IndigoDeep = Color.FromArgb(49, 46, 129);
    public static readonly Color IndigoSoft = Color.FromArgb(224, 231, 255);
    public static readonly Color Ink = Color.FromArgb(30, 27, 75);
    public static readonly Color Paper = Color.FromArgb(247, 247, 252);

    public static readonly Palette Light = new(Color.FromArgb(224, 231, 255), Color.FromArgb(30, 27, 75), Color.FromArgb(165, 180, 252), Indigo, Color.White, Color.FromArgb(207, 216, 252));
    public static readonly Palette Dark = new(Color.FromArgb(39, 42, 69), Color.FromArgb(224, 231, 255), Color.FromArgb(63, 67, 128), Color.FromArgb(99, 102, 241), Color.White, Color.FromArgb(52, 56, 92));

    public static Palette For(GrpTheme theme) => theme switch
    {
        GrpTheme.Light => Light,
        GrpTheme.Dark => Dark,
        _ => SystemUsesDark() ? Dark : Light,
    };

    /// <summary>Windows' "Choose your app mode" setting.</summary>
    public static bool SystemUsesDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
