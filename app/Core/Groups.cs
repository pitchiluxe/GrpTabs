namespace GrpTabs.Core;

/// <summary>One tabbed window: its members in tab order and the one that is shown.</summary>
public sealed class TabGroup
{
    public required string Id { get; init; }
    public List<IntPtr> Members { get; } = new();
    public IntPtr Active { get; set; }
}

/// <summary>
/// Tab groups as data, with no Windows calls, so the rules can be tested:
/// join, detach, activate, cycle and forget. The engine applies the result.
/// </summary>
public sealed class GroupModel
{
    private int seq;
    public List<TabGroup> Groups { get; } = new();

    public TabGroup? GroupOf(IntPtr w) => Groups.FirstOrDefault(g => g.Members.Contains(w));

    /// <summary>The window's tabs: its group's members, or just itself.</summary>
    public IReadOnlyList<IntPtr> TabsFor(IntPtr w) => GroupOf(w)?.Members ?? (IReadOnlyList<IntPtr>)new[] { w };

    private void Prune() => Groups.RemoveAll(g => g.Members.Count < 2);

    /// <summary>Takes a window out of its group; the next tab becomes active if it was.</summary>
    public void Detach(IntPtr w)
    {
        var g = GroupOf(w);
        if (g is null) return;
        int i = g.Members.IndexOf(w);
        g.Members.RemoveAt(i);
        if (g.Active == w) g.Active = g.Members.Count > 0 ? g.Members[Math.Min(i, g.Members.Count - 1)] : IntPtr.Zero;
        Prune();
    }

    /// <summary>Puts <paramref name="w"/> into <paramref name="target"/>'s group at tab index <paramref name="at"/> and makes it active. False when nothing changes.</summary>
    public bool Join(IntPtr w, IntPtr target, int? at = null)
    {
        if (w == target) return false;
        var tg = GroupOf(target);
        if (tg is not null && tg.Members.Contains(w))
        {
            if (at is null) return false;
            int from = tg.Members.IndexOf(w);
            tg.Members.RemoveAt(from);
            int to = at.Value > from ? at.Value - 1 : at.Value;
            tg.Members.Insert(Math.Clamp(to, 0, tg.Members.Count), w);
            return true;
        }
        Detach(w);
        var g = GroupOf(target);
        if (g is null)
        {
            g = new TabGroup { Id = $"grp-{++seq}", Active = target };
            g.Members.Add(target);
            Groups.Add(g);
        }
        g.Members.Insert(at is null ? g.Members.Count : Math.Clamp(at.Value, 0, g.Members.Count), w);
        g.Active = w;
        return true;
    }

    public void Activate(IntPtr w)
    {
        var g = GroupOf(w);
        if (g is not null) g.Active = w;
    }

    /// <summary>The tab <paramref name="dir"/> steps from the active one, wrapping round; null if not grouped.</summary>
    public IntPtr? Cycle(IntPtr w, int dir)
    {
        var g = GroupOf(w);
        if (g is null) return null;
        int i = g.Members.IndexOf(g.Active);
        int n = g.Members.Count;
        return g.Members[((i + dir) % n + n) % n];
    }

    /// <summary>A window closed: drop it, and any members that no longer exist.</summary>
    public void Forget(IntPtr w, Func<IntPtr, bool> alive)
    {
        Detach(w);
        foreach (var g in Groups)
        {
            g.Members.RemoveAll(m => !alive(m));
            if (!g.Members.Contains(g.Active)) g.Active = g.Members.FirstOrDefault();
        }
        Prune();
    }

    /// <summary>Windows hidden behind another tab of their group.</summary>
    public IEnumerable<IntPtr> HiddenMembers() => Groups.SelectMany(g => g.Members.Where(m => m != g.Active));
}
