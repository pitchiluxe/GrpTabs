using System.Globalization;

namespace GrpTabs.Core;

/// <summary>The colours of a tab strip, all derived from one chosen colour.</summary>
public readonly record struct TabPalette(Color Tab, Color TabInk, Color Line, Color On, Color OnInk, Color Hover, Color Close);

/// <summary>Tab colour choices and the arithmetic that turns one colour into a readable palette.</summary>
public static class TabColors
{
    public static readonly Color Indigo = Color.FromArgb(79, 70, 229);

    /// <summary>The rainbow, in spectrum order: with the default setting each tab takes the next one.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Rainbow = new[]
    {
        ("Red", "#DC2626"), ("Orange", "#C2410C"), ("Gold", "#A16207"), ("Green", "#15803D"), ("Teal", "#0F766E"),
        ("Blue", "#1D4ED8"), ("Indigo", "#4F46E5"), ("Violet", "#7E22CE"), ("Pink", "#BE185D"),
    };

    /// <summary>The single-colour swatches in Settings > Appearance and the tab menu.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = Rainbow.Append(("Graphite", "#374151")).ToArray();

    /// <summary>The order tabs take the rainbow colours in: neighbours far apart on the colour wheel, so tabs side by side never look alike.</summary>
    private static readonly int[] SlotOrder = { 0, 5, 2, 7, 3, 8, 4, 6, 1 };

    public static Color RainbowColor(int slot) => Parse(Rainbow[SlotOrder[((slot % Rainbow.Count) + Rainbow.Count) % Rainbow.Count]].Hex)!.Value;

    /// <summary>
    /// The rainbow slot for a window joining tabs that already use <paramref name="used"/>:
    /// the first free slot from <paramref name="next"/> on, so tabs side by side never share a colour
    /// (until a group has more tabs than the rainbow has colours).
    /// </summary>
    public static int PickSlot(IEnumerable<int> used, int next)
    {
        int n = Rainbow.Count;
        var taken = used.Select(u => ((u % n) + n) % n).ToHashSet();
        int start = ((next % n) + n) % n;
        for (int i = 0; i < n; i++)
        {
            int s = (start + i) % n;
            if (!taken.Contains(s)) return s;
        }
        return start;
    }

    /// <summary>"" (rainbow), "accent" or "#RRGGBB" (upper case); anything else becomes "" (rainbow).</summary>
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
        // Other tabs keep enough of their colour to be told apart at a glance (rainbow tabs).
        var tab = dark ? Mix(on, bg, 0.5) : Mix(on, bg, 0.7);
        var hover = dark ? Mix(on, bg, 0.36) : Mix(on, bg, 0.56);
        var line = dark ? Mix(on, bg, 0.25) : Mix(on, bg, 0.4);
        return new TabPalette(tab, InkOn(tab), line, on, InkOn(on), hover, Color.FromArgb(196, 43, 28));
    }
}
