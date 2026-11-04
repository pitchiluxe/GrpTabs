using GrpTabs.Core;

namespace GrpTabs.Tests;

public class TabColorTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("accent", "accent")]
    [InlineData("ACCENT", "accent")]
    [InlineData("#0f766e", "#0F766E")]
    [InlineData("0F766E", "")]
    [InlineData("#12345", "")]
    [InlineData("red", "")]
    [InlineData(null, "")]
    public void Normalizes_colour_choices(string? input, string expected) => Assert.Equal(expected, TabColors.Normalize(input));

    [Fact]
    public void Every_preset_gives_readable_tabs_in_light_and_dark()
    {
        foreach (var (name, hex) in TabColors.Presets)
            foreach (bool dark in new[] { false, true })
            {
                var p = TabColors.Derive(TabColors.Parse(hex)!.Value, dark);
                Assert.True(TabColors.Contrast(p.On, p.OnInk) >= 4.5, $"{name} active tab text ({(dark ? "dark" : "light")})");
                Assert.True(TabColors.Contrast(p.Tab, p.TabInk) >= 4.5, $"{name} other tab text ({(dark ? "dark" : "light")})");
            }
    }

    [Fact]
    public void Text_colour_follows_the_background()
    {
        Assert.Equal(Color.White, TabColors.InkOn(Color.FromArgb(49, 46, 129)));
        Assert.NotEqual(Color.White, TabColors.InkOn(Color.FromArgb(253, 224, 71)));
    }

    [Fact]
    public void Settings_repair_a_bad_colour_and_keep_new_switches()
    {
        // SmoothSwitch was a 1.1 setting: an old settings file that still has it loads fine.
        var s = Settings.Parse("""{"TabColor":"#zzzzzz","CloseButtons":false,"SmoothSwitch":false}""");
        Assert.Equal("", s.TabColor);
        Assert.False(s.CloseButtons);
        var d = new Settings();
        Assert.True(d.WheelSwitches && d.NumberHotkeys && d.HideInFullScreen && d.CloseButtons);
        Assert.Equal("", d.TabColor); // rainbow by default
    }

    [Fact]
    public void Older_settings_move_to_rainbow_once_and_later_choices_stay()
    {
        Assert.Equal("", Settings.Parse("""{"TabColor":"#0F766E"}""").TabColor);
        var kept = Settings.Parse("""{"TabColor":"#0F766E","Version":2}""");
        Assert.Equal("#0F766E", kept.TabColor);
        Assert.Equal(2, kept.Version);
        var fresh = new Settings { TabColor = "#B45309" };
        Assert.Equal("#B45309", Settings.Parse(fresh.ToJson()).TabColor);
    }

    [Fact]
    public void Rainbow_gives_neighbouring_tabs_different_colours()
    {
        Assert.Equal(0, TabColors.PickSlot(new int[0], 0));
        Assert.Equal(3, TabColors.PickSlot(new[] { 0, 1 }, 3));
        Assert.Equal(2, TabColors.PickSlot(new[] { 0, 1 }, 1));   // 1 is taken: next free
        Assert.Equal(0, TabColors.PickSlot(new[] { 7, 8 }, 7));   // wraps round
        var all = Enumerable.Range(0, TabColors.Rainbow.Count).ToArray();
        Assert.Equal(4, TabColors.PickSlot(all, 4));              // more tabs than colours: repeats
        var picked = new List<int>();
        for (int i = 0; i < TabColors.Rainbow.Count; i++) picked.Add(TabColors.PickSlot(picked, 0));
        Assert.Equal(TabColors.Rainbow.Count, picked.Distinct().Count());
    }

    [Fact]
    public void Rainbow_colours_are_distinct_presets()
    {
        Assert.Equal(TabColors.Rainbow.Count, TabColors.Rainbow.Select(r => r.Hex).Distinct().Count());
        Assert.All(TabColors.Rainbow, r => Assert.Contains(TabColors.Presets, p => p.Hex == r.Hex));
        Assert.Equal(TabColors.Parse("#DC2626"), TabColors.RainbowColor(TabColors.Rainbow.Count));
        // Every rainbow colour is used once per round, and neighbours are never next to each other in the spectrum.
        var round = Enumerable.Range(0, TabColors.Rainbow.Count).Select(TabColors.RainbowColor).ToList();
        Assert.Equal(TabColors.Rainbow.Count, round.Distinct().Count());
        var spectrum = TabColors.Rainbow.Select(r => TabColors.Parse(r.Hex)!.Value).ToList();
        for (int i = 1; i < round.Count; i++)
            Assert.True(Math.Abs(spectrum.IndexOf(round[i]) - spectrum.IndexOf(round[i - 1])) > 1, $"tabs {i - 1} and {i} are spectrum neighbours");
    }
}

public class FullScreenRuleTests
{
    [Fact]
    public void Full_screen_windows_get_no_tabs_unless_turned_off()
    {
        var g = new GroupModel();
        var w = new WinFacts(1, "game.exe", false, false, FullScreen: true);
        Assert.Equal(StripMode.None, Rules.Mode(new Settings(), g, w));
        Assert.Equal(StripMode.Hover, Rules.Mode(new Settings { HideInFullScreen = false }, g, w));
    }
}
