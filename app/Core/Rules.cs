namespace GrpTabs.Core;

/// <summary>Facts about a window that the rules need (kept apart from Win32 so they can be tested).</summary>
public readonly record struct WinFacts(IntPtr Hwnd, string Exe, bool Maximized, bool Minimized);

public enum StripMode { Show, Hover, None }

/// <summary>When a window shows tabs, what can be grouped, and automatic grouping.</summary>
public static class Rules
{
    public static StripMode Mode(Settings st, GroupModel groups, WinFacts w)
    {
        if (w.Minimized || st.IsException(w.Exe)) return StripMode.None;
        if (w.Maximized && st.HideWhenMaximized) return StripMode.None;
        var when = groups.GroupOf(w.Hwnd) is not null ? st.GroupedTabs : st.SingleTabs;
        return when switch { TabsWhen.Always => StripMode.Show, TabsWhen.Hover => StripMode.Hover, _ => StripMode.None };
    }

    public static bool CanJoin(Settings st, WinFacts w, WinFacts target) =>
        w.Hwnd != target.Hwnd && !st.IsException(w.Exe) && !st.IsException(target.Exe);

    /// <summary>A new window of an auto-group program joins an open, not minimized window of the same program.</summary>
    public static IntPtr? AutoGroupTarget(Settings st, WinFacts opened, IEnumerable<WinFacts> others)
    {
        if (!st.IsAutoGroup(opened.Exe) || st.IsException(opened.Exe)) return null;
        foreach (var o in others)
            if (o.Hwnd != opened.Hwnd && !o.Minimized && string.Equals(o.Exe, opened.Exe, StringComparison.OrdinalIgnoreCase)) return o.Hwnd;
        return null;
    }
}
