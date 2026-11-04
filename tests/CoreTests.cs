using GrpTabs.Core;

namespace GrpTabs.Tests;

public class GroupModelTests
{
    private static readonly IntPtr A = 1, B = 2, C = 3, D = 4;

    [Fact]
    public void Dragging_a_tab_onto_a_window_makes_a_group_with_the_dragged_window_active()
    {
        var g = new GroupModel();
        Assert.True(g.Join(B, A));
        var grp = g.GroupOf(A)!;
        Assert.Equal(new[] { A, B }, grp.Members);
        Assert.Equal(B, grp.Active);
        g.Join(C, B, 0);
        Assert.Equal(new[] { C, A, B }, g.TabsFor(A));
        Assert.Equal(new[] { A, B }, g.HiddenMembers().OrderBy(x => x).ToArray());
        Assert.False(g.Join(A, A));
    }

    [Fact]
    public void Moving_a_window_between_groups_dissolves_a_group_of_one()
    {
        var g = new GroupModel();
        g.Join(B, A);
        g.Join(D, C);
        g.Join(B, C);
        Assert.Null(g.GroupOf(A));
        Assert.Equal(new[] { C, D, B }, g.TabsFor(C));
    }

    [Fact]
    public void Reordering_inside_a_group()
    {
        var g = new GroupModel();
        g.Join(B, A);
        g.Join(C, A);
        g.Join(A, B, 3);
        Assert.Equal(new[] { B, C, A }, g.TabsFor(A));
        g.Join(A, B, 0);
        Assert.Equal(new[] { A, B, C }, g.TabsFor(A));
    }

    [Fact]
    public void Detach_and_forget_pick_the_neighbouring_tab()
    {
        var g = new GroupModel();
        g.Join(B, A);
        g.Join(C, A);
        g.Activate(B);
        g.Detach(B);
        Assert.Equal(C, g.GroupOf(A)!.Active);
        g.Forget(C, _ => true);
        Assert.Empty(g.Groups);
    }

    [Fact]
    public void Forget_drops_members_that_no_longer_exist()
    {
        var g = new GroupModel();
        g.Join(B, A);
        g.Join(C, A);
        g.Forget(C, h => h != B);
        Assert.Empty(g.Groups);
    }

    [Fact]
    public void Cycling_wraps_round_in_tab_order()
    {
        var g = new GroupModel();
        g.Join(B, A);
        g.Join(C, A);
        g.Activate(C);
        Assert.Equal(A, g.Cycle(A, 1));
        Assert.Equal(B, g.Cycle(A, -1));
        Assert.Null(g.Cycle(D, 1));
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("ctrl+alt+pagedown", "Ctrl+Alt+PageDown")]
    [InlineData("Ctrl+Shift+F5", "Ctrl+Shift+F5")]
    [InlineData("Alt+7", "Alt+7")]
    [InlineData("Win+Alt+Left", "Win+Alt+Left")]
    public void Parses_and_prints(string text, string expected) => Assert.Equal(expected, Hotkey.Parse(text)!.Value.ToString());

    [Theory]
    [InlineData("D")]
    [InlineData("Shift+D")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Nonsense")]
    [InlineData("")]
    public void Refuses_hotkeys_without_a_modifier_or_key(string text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void From_a_key_press()
    {
        Assert.Equal("Ctrl+Alt+D", Hotkey.FromKeys(Keys.Control | Keys.Alt | Keys.D)!.Value.ToString());
        Assert.Null(Hotkey.FromKeys(Keys.Control | Keys.ControlKey));
        Assert.Null(Hotkey.FromKeys(Keys.D));
    }

    [Fact]
    public void Clashes_are_found()
    {
        var map = Settings.DefaultHotkeys();
        Assert.Empty(Hotkey.Clashes(map));
        map[HotkeyAction.Pause] = "Ctrl+Alt+D";
        Assert.Single(Hotkey.Clashes(map));
    }

    [Fact]
    public void Modifiers_never_repeat()
    {
        var hk = Hotkey.Parse("Ctrl+Alt+G")!.Value;
        Assert.Equal(Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, hk.Modifiers);
    }
}

public class SettingsTests
{
    [Fact]
    public void A_broken_file_falls_back_to_defaults() => Assert.Equal(new Settings().ToJson(), Settings.Parse("{not json").ToJson());

    [Fact]
    public void Out_of_range_values_are_repaired()
    {
        var s = Settings.Parse("""{"IdleOpacity":5,"TabWidth":999,"Exceptions":["notepad.exe","NOTEPAD.EXE"," "],"Hotkeys":{"Next":"PageDown","Detach":""}}""");
        Assert.Equal(30, s.IdleOpacity);
        Assert.Equal(260, s.TabWidth);
        Assert.Equal(new[] { "notepad.exe" }, s.Exceptions);
        Assert.Equal("Ctrl+Alt+PageDown", s.Hotkeys[HotkeyAction.Next]);
        Assert.Equal("", s.Hotkeys[HotkeyAction.Detach]);
        Assert.Equal("Ctrl+Alt+G", s.Hotkeys[HotkeyAction.Pause]);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var s = new Settings { Theme = GrpTheme.Dark, SingleTabs = TabsWhen.Never, AutoGroup = { "chrome.exe" } };
        var back = Settings.Parse(s.ToJson());
        Assert.Equal(GrpTheme.Dark, back.Theme);
        Assert.Equal(TabsWhen.Never, back.SingleTabs);
        Assert.Contains("chrome.exe", back.AutoGroup);
        Assert.Contains("\"Dark\"", s.ToJson());
    }

    [Fact]
    public void Program_lists_ignore_case()
    {
        var s = new Settings();
        Assert.True(s.IsException("TASKMGR.EXE"));
        Assert.False(s.IsAutoGroup("notepad.exe"));
    }
}

public class RulesTests
{
    private static WinFacts W(int h, string exe = "notepad.exe", bool max = false, bool min = false) => new(h, exe, max, min);

    [Fact]
    public void Tabs_follow_the_settings_and_exceptions()
    {
        var st = new Settings();
        var g = new GroupModel();
        Assert.Equal(StripMode.Hover, Rules.Mode(st, g, W(1)));
        g.Join(2, 1);
        Assert.Equal(StripMode.Show, Rules.Mode(st, g, W(1)));
        Assert.Equal(StripMode.None, Rules.Mode(new Settings { HideWhenMaximized = true }, g, W(1, max: true)));
        Assert.Equal(StripMode.None, Rules.Mode(st, g, W(9, "Taskmgr.exe")));
        Assert.Equal(StripMode.None, Rules.Mode(st, g, W(1, min: true)));
        Assert.False(Rules.CanJoin(st, W(9, "Taskmgr.exe"), W(1)));
        Assert.False(Rules.CanJoin(st, W(1), W(1)));
        Assert.True(Rules.CanJoin(st, W(2), W(1)));
    }

    [Fact]
    public void Auto_group_joins_an_open_window_of_the_same_program()
    {
        var st = new Settings { AutoGroup = { "notepad.exe" } };
        var others = new[] { W(5, "chrome.exe"), W(6, "notepad.exe", min: true), W(7, "NOTEPAD.EXE") };
        Assert.Equal((IntPtr)7, Rules.AutoGroupTarget(st, W(8), others));
        Assert.Null(Rules.AutoGroupTarget(new Settings(), W(8), others));
        Assert.Null(Rules.AutoGroupTarget(st, W(8, "chrome.exe"), others));
    }
}
