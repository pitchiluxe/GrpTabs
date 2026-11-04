using GrpTabs.Core;
using Microsoft.Win32;

namespace GrpTabs.Ui;

/// <summary>GrpTabs colours: the brand indigo for the settings window, and the tab palette from Settings > Appearance.</summary>
internal static class Palette
{
    public static readonly Color Indigo = Color.FromArgb(79, 70, 229);
    public static readonly Color IndigoDeep = Color.FromArgb(49, 46, 129);
    public static readonly Color IndigoSoft = Color.FromArgb(224, 231, 255);
    public static readonly Color Ink = Color.FromArgb(30, 27, 75);
    public static readonly Color Paper = Color.FromArgb(247, 247, 252);

    /// <summary>The tab colours for these settings: the chosen colour (or Windows accent), light or dark.</summary>
    public static TabPalette For(Settings st) => TabColors.Derive(BaseColor(st.TabColor), IsDark(st.Theme));

    /// <summary>The palette for one tab of colour <paramref name="c"/>, light or dark.</summary>
    public static TabPalette For(Color c, Settings st) => TabColors.Derive(c, IsDark(st.Theme));

    /// <summary>The one colour of a single-colour setting (rainbow and unknown values give indigo).</summary>
    public static Color BaseColor(string tabColor) =>
        tabColor == "accent" ? WindowsAccent() ?? TabColors.Indigo : TabColors.Parse(tabColor) ?? TabColors.Indigo;

    public static bool IsDark(GrpTheme theme) => theme switch { GrpTheme.Dark => true, GrpTheme.Light => false, _ => SystemUsesDark() };

    /// <summary>Windows' "Choose your app mode" setting.</summary>
    public static bool SystemUsesDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>The accent colour from Settings > Personalization > Colors (stored as 0xAABBGGRR).</summary>
    public static Color? WindowsAccent()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (k?.GetValue("AccentColor") is not int v) return null;
        return Color.FromArgb(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF);
    }
}
