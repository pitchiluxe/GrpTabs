using System.Globalization;

namespace GrpTabs.Core;

/// <summary>The colours of a tab strip, all derived from one chosen colour.</summary>
public readonly record struct TabPalette(Color Tab, Color TabInk, Color Line, Color On, Color OnInk, Color Hover, Color Close);

/// <summary>Tab colour choices and the arithmetic that turns one colour into a readable palette.</summary>
public static class TabColors
{
    public static readonly Color Indigo = Color.FromArgb(79, 70, 229);

    /// <summary>The swatches in Settings > Appearance.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new[]
    {
        ("Indigo", "#4F46E5"), ("Ocean", "#0369A1"), ("Teal", "#0F766E"), ("Forest", "#15803D"),
        ("Amber", "#B45309"), ("Coral", "#C2410C"), ("Rose", "#BE123C"), ("Plum", "#86198F"), ("Graphite", "#374151"),
    };

    /// <summary>"", "accent" or "#RRGGBB" (upper case); anything else becomes "".</summary>
    public static string Normalize(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Equals("accent", StringComparison.OrdinalIgnoreCase)) return "accent";
        return Parse(v) is not null ? v.ToUpperInvariant() : "";
    }

    public static Color? Parse(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#') return null;
        return int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
            ? Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) : null;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Color c)
    {
        static double Lin(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>White or near-black, whichever reads better on <paramref name="bg"/>.</summary>
    public static Color InkOn(Color bg)
    {
        var dark = Color.FromArgb(24, 22, 48);
        return Contrast(Color.White, bg) >= Contrast(dark, bg) ? Color.White : dark;
    }

    /// <summary>A full palette from one colour: the active tab is the colour, the others a tint of it.</summary>
    public static TabPalette Derive(Color on, bool dark)
    {
        var bg = dark ? Color.FromArgb(32, 33, 44) : Color.White;
        var tab = dark ? Mix(on, bg, 0.72) : Mix(on, bg, 0.84);
        var hover = dark ? Mix(on, bg, 0.58) : Mix(on, bg, 0.72);
        var line = dark ? Mix(on, bg, 0.45) : Mix(on, bg, 0.55);
        return new TabPalette(tab, InkOn(tab), line, on, InkOn(on), hover, Color.FromArgb(196, 43, 28));
    }
}
