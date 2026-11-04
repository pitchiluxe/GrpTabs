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
        var s = Settings.Parse("""{"TabColor":"#zzzzzz","CloseButtons":false,"SmoothSwitch":false}""");
        Assert.Equal("", s.TabColor);
        Assert.False(s.CloseButtons);
        Assert.False(s.SmoothSwitch);
        var d = new Settings();
        Assert.True(d.WheelSwitches && d.NumberHotkeys && d.HideInFullScreen && d.SmoothSwitch && d.CloseButtons);
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
