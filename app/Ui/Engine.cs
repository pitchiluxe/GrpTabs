using System.Text.Json;
using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// GrpTabs on the real desktop. Follows top-level windows through WinEvent
/// hooks, keeps one tab strip per shown window, and applies the group model:
/// a group shares one frame, the active tab's window is shown and the others
/// are hidden behind it. Every window it hides is written to disk first, so
/// exiting, updating or even a crash never loses a window.
/// </summary>
internal sealed class Engine : IDisposable
{
    private readonly GroupModel groups = new();
    private readonly Dictionary<IntPtr, TabStrip> strips = new();
    private readonly HashSet<IntPtr> hiddenByUs = new();
    /// <summary>Windows we just showed: DWM keeps them cloaked during the show animation, which is not a reason to drop their tabs.</summary>
    private readonly Dictionary<IntPtr, long> shownAt = new();
    private readonly Dictionary<IntPtr, (Native.WINDOWPLACEMENT Place, Native.RECT Frame)> lastPlace = new();
    private readonly Dictionary<IntPtr, Image?> icons = new();
    private readonly List<IntPtr> hooks = new();
    private readonly Native.WinEventProc proc;
    private readonly System.Windows.Forms.Timer hoverTimer = new() { Interval = 120 };
    private readonly HotkeyWindow hotkeys;
    private IntPtr hovered;
    private TabStrip? hoveredStrip;
    private DragGhost? ghost;
    private TabStrip? dropStrip;
    private IntPtr dragging;
    private bool selfChange;

    public Settings Settings { get; private set; }
    public bool Paused { get; private set; }
    public event Action? Changed;

    public static string HiddenFile => Path.Combine(Settings.Folder, "hidden.json");

    public Engine(Settings settings)
    {
        Settings = settings;
        proc = OnWinEvent;
        hotkeys = new HotkeyWindow(OnHotkey, OnNumberHotkey);
        hoverTimer.Tick += (_, _) => HoverTick();
    }

    public void Start()
    {
        RecoverHidden();
        foreach (var (min, max) in new (uint, uint)[]
        {
            (Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND),
            (Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND),
            (Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND),
            (Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE),
            (Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_NAMECHANGE),
            (Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED),
        })
        {
            var h = Native.SetWinEventHook(min, max, IntPtr.Zero, proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (h != IntPtr.Zero) hooks.Add(h);
        }
        foreach (var w in WindowInfo.TopLevel()) Refresh(w);
        hotkeys.Register(Settings, Paused);
        hoverTimer.Start();
    }

    public List<string> HotkeyFailures => hotkeys.Failures;

    public void Apply(Settings st)
    {
        Settings = st;
        hotkeys.Register(st, Paused);
        foreach (var w in WindowInfo.TopLevel()) Refresh(w);
        foreach (var s in strips.Values) s.Reposition();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ events

    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || hwnd == IntPtr.Zero) return;
        if (strips.Values.Any(s => s.IsHandleCreated && s.Handle == hwnd)) return;
        try { Handle(ev, hwnd); }
        catch (Exception) { /* a window can vanish between the event and the call: ignore it */ }
    }

    private void Handle(uint ev, IntPtr hwnd)
    {
        switch (ev)
        {
            case Native.EVENT_OBJECT_LOCATIONCHANGE:
                if (strips.TryGetValue(hwnd, out var s)) { Remember(hwnd); s.Reposition(); }
                else if (groups.GroupOf(hwnd) is not null && !hiddenByUs.Contains(hwnd)) Remember(hwnd);
                break;
            case Native.EVENT_SYSTEM_MOVESIZEEND:
                if (strips.TryGetValue(hwnd, out var s2)) { Remember(hwnd); s2.Reposition(); }
                break;
            case Native.EVENT_SYSTEM_FOREGROUND:
                Refresh(hwnd);
                // Any window coming forward changes the stacking: every strip re-seats itself above its window.
                foreach (var s5 in strips.Values) s5.KeepAbove();
                break;
            case Native.EVENT_OBJECT_SHOW:
            case Native.EVENT_OBJECT_UNCLOAKED:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
                if (selfChange) break;
                if (hiddenByUs.Contains(hwnd) && Native.IsWindowVisible(hwnd))
                {
                    // The app showed a window we had hidden (it was restored from its own tray icon, for example): make it the active tab.
                    hiddenByUs.Remove(hwnd);
                    SaveHidden();
                    var g = groups.GroupOf(hwnd);
                    if (g is not null && g.Active != hwnd) ActivateTab(hwnd);
                    break;
                }
                bool isNew = !strips.ContainsKey(hwnd) && groups.GroupOf(hwnd) is null;
                Refresh(hwnd);
                if (isNew && ev == Native.EVENT_OBJECT_SHOW && !Paused) AutoGroup(hwnd);
                break;
            case Native.EVENT_OBJECT_HIDE:
            case Native.EVENT_OBJECT_CLOAKED:
                if (selfChange || hiddenByUs.Contains(hwnd)) break;
                if (ev == Native.EVENT_OBJECT_CLOAKED && JustShown(hwnd)) { RefreshSoon(hwnd); break; }
                if (groups.GroupOf(hwnd) is { } grp && grp.Active == hwnd && !Native.IsWindowVisible(hwnd) && Native.IsWindow(hwnd))
                {
                    // The app hid its own window (minimize to tray): the group's next tab takes its place.
                    var before = grp.Members.ToList();
                    groups.Detach(hwnd);
                    HandOff(hwnd, before);
                }
                DropStrip(hwnd);
                break;
            case Native.EVENT_SYSTEM_MINIMIZESTART:
                if (strips.TryGetValue(hwnd, out var s3)) s3.Hide();
                break;
            case Native.EVENT_OBJECT_DESTROY:
                Destroyed(hwnd);
                break;
            case Native.EVENT_OBJECT_NAMECHANGE:
                var owner = groups.GroupOf(hwnd)?.Active ?? hwnd;
                if (strips.TryGetValue(owner, out var s4)) s4.Reposition();
                else Refresh(hwnd);
                break;
        }
    }

    private void Destroyed(IntPtr hwnd)
    {
        icons.Remove(hwnd);
        hiddenByUs.Remove(hwnd);
        var g = groups.GroupOf(hwnd);
        if (g is not null)
        {
            var before = g.Members.ToList();
            bool wasActive = g.Active == hwnd;
            groups.Forget(hwnd, Native.IsWindow);
            if (wasActive) HandOff(hwnd, before);
            else foreach (var m in before) if (m != hwnd) Refresh(m);
        }
        DropStrip(hwnd);
        lastPlace.Remove(hwnd);
        SaveHidden();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ strips

    public IntPtr ActiveOf(IntPtr target) => groups.GroupOf(target)?.Active ?? target;

    public IReadOnlyList<TabGroup> Groups => groups.Groups;

    /// <summary>Create, update or remove the strip of a shown window according to the settings.</summary>
    public void Refresh(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd) || hiddenByUs.Contains(hwnd)) { if (strips.ContainsKey(hwnd)) Log.Write($"drop {hwnd}: gone or hidden by us"); DropStrip(hwnd); return; }
        if (Native.IsWindowVisible(hwnd) && Native.IsCloaked(hwnd) && strips.ContainsKey(hwnd) && JustShown(hwnd)) { RefreshSoon(hwnd); return; }
        if (!WindowInfo.Eligible(hwnd)) { if (strips.ContainsKey(hwnd)) Log.Write($"drop {hwnd}: not eligible visible={Native.IsWindowVisible(hwnd)} cloaked={Native.IsCloaked(hwnd)} iconic={Native.IsIconic(hwnd)}"); DropStrip(hwnd); return; }
        var mode = Paused ? StripMode.None : Rules.Mode(Settings, groups, WindowInfo.Facts(hwnd));
        if (mode == StripMode.None) { if (strips.ContainsKey(hwnd)) Log.Write($"drop {hwnd}: mode none"); DropStrip(hwnd); return; }
        if (!strips.TryGetValue(hwnd, out var strip))
        {
            strip = new TabStrip(this, hwnd);
            strips[hwnd] = strip;
            Remember(hwnd);
        }
        strip.SetTabs(groups.TabsFor(hwnd));
        strip.Reposition();
        bool show = mode == StripMode.Show || hovered == hwnd || hoveredStrip == strip;
        Log.Write($"refresh {hwnd} mode={mode} show={show} visible={strip.Visible} bounds={strip.Bounds}");
        if (show && !strip.Visible && !Native.IsIconic(hwnd)) strip.Show();
        else if (!show && strip.Visible) strip.Hide();
    }

    /// <summary>
    /// The group's frame now belongs to another window: the same strip follows it,
    /// so the tabs never disappear and reappear while you switch.
    /// </summary>
    private void MoveStrip(IntPtr from, IntPtr to)
    {
        if (from == to || !strips.Remove(from, out var s)) return;
        if (strips.Remove(to, out var old)) { old.Close(); old.Dispose(); }
        s.Retarget(to);
        strips[to] = s;
        s.SetTabs(groups.TabsFor(to));
        if (Native.IsWindowVisible(to)) s.Reposition();
    }

    /// <summary>Refresh again shortly: a window that was just shown can report itself not ready for a moment.</summary>
    private void RefreshSoon(IntPtr hwnd)
    {
        var t = new System.Windows.Forms.Timer { Interval = 150 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); Refresh(hwnd); };
        t.Start();
    }

    /// <summary>True for a short while after GrpTabs showed the window, while DWM still cloaks it for the show animation.</summary>
    private bool JustShown(IntPtr hwnd)
    {
        if (!shownAt.TryGetValue(hwnd, out var at)) return false;
        if (Environment.TickCount64 - at < 800) return true;
        shownAt.Remove(hwnd);
        return false;
    }

    private void DropStrip(IntPtr hwnd)
    {
        if (!strips.Remove(hwnd, out var s)) return;
        if (hoveredStrip == s) hoveredStrip = null;
        s.Close();
        s.Dispose();
    }

    public void StripHover(TabStrip s, bool on)
    {
        hoveredStrip = on ? s : (hoveredStrip == s ? null : hoveredStrip);
        if (!on) Refresh(s.Target);
    }

    /// <summary>Hover mode: show a window's tabs while the pointer is over it.</summary>
    private void HoverTick()
    {
        // Backstop for stacking changes that send no event (another app reordering its own windows).
        foreach (var s in strips.Values) s.KeepAbove();
        if (Paused || dragging != IntPtr.Zero) return;
        Native.GetCursorPos(out var p);
        var root = Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT);
        if (strips.Values.FirstOrDefault(s => s.IsHandleCreated && s.Handle == root) is { } overStrip) root = overStrip.Target;
        if (root == hovered) return;
        Log.Write($"hover {root} ({Native.Title(root)}) eligible={WindowInfo.Eligible(root)} strip={strips.ContainsKey(root)}");
        var old = hovered;
        hovered = root;
        if (old != IntPtr.Zero) Refresh(old);
        if (root != IntPtr.Zero && (strips.ContainsKey(root) || WindowInfo.Eligible(root))) Refresh(root);
    }

    public Image? IconOf(IntPtr hwnd)
    {
        if (icons.TryGetValue(hwnd, out var img)) return img;
        img = null;
        var h = Native.SmallIcon(hwnd);
        if (h != IntPtr.Zero) { try { using var ico = Icon.FromHandle(h); img = ico.ToBitmap(); } catch (Exception) { img = null; } }
        icons[hwnd] = img;
        return img;
    }

    private void Remember(IntPtr hwnd)
    {
        var wp = Native.WINDOWPLACEMENT.Default;
        if (Native.GetWindowPlacement(hwnd, ref wp)) lastPlace[hwnd] = (wp, Native.Frame(hwnd));
    }

    // ------------------------------------------------------------------ grouping

    /// <summary>Show <paramref name="to"/> exactly where <paramref name="from"/> is (its visible frame, maximized or not).</summary>
    private void ShowInPlaceOf(IntPtr from, IntPtr to)
    {
        shownAt[to] = Environment.TickCount64;
        if (!lastPlace.TryGetValue(from, out var src) || Native.IsWindow(from)) { Remember(from); src = lastPlace.GetValueOrDefault(from); }
        if (src.Place.length == 0) { Show(to); return; }
        var wp = src.Place;
        bool max = wp.showCmd == Native.SW_SHOWMAXIMIZED;
        wp.showCmd = max ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOWNORMAL;
        wp.flags = 0;
        selfChange = true;
        try
        {
            hiddenByUs.Remove(to);
            Native.SetWindowPlacement(to, ref wp);
            if (!max)
            {
                // Apps differ in their invisible borders: line the visible frames up exactly.
                var now = Native.Frame(to);
                Native.GetWindowRect(to, out var outer);
                var want = src.Frame;
                int dl = want.Left - now.Left, dt = want.Top - now.Top, dr = want.Right - now.Right, db = want.Bottom - now.Bottom;
                if (dl != 0 || dt != 0 || dr != 0 || db != 0)
                    Native.SetWindowPos(to, IntPtr.Zero, outer.Left + dl, outer.Top + dt, outer.Width - dl + dr, outer.Height - dt + db, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            Native.ShowWindow(to, max ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOW);
        }
        finally { selfChange = false; }
        Remember(to);
    }

    private void Show(IntPtr hwnd)
    {
        selfChange = true;
        try { hiddenByUs.Remove(hwnd); Native.ShowWindow(hwnd, Native.SW_SHOW); }
        finally { selfChange = false; }
    }

    private void HideMember(IntPtr hwnd)
    {
        Remember(hwnd);
        hiddenByUs.Add(hwnd);
        SaveHidden();
        DropStrip(hwnd);
        selfChange = true;
        try { Native.ShowWindow(hwnd, Native.SW_HIDE); }
        finally { selfChange = false; }
    }

    /// <summary>The group's next tab takes the place of a window that left it (closed, dragged out, hidden by its app).</summary>
    private void HandOff(IntPtr leaving, List<IntPtr> before)
    {
        var rest = before.Where(m => m != leaving && Native.IsWindow(m)).ToList();
        if (rest.Count == 0) return;
        var next = groups.GroupOf(rest[0])?.Active ?? rest[0];
        ShowInPlaceOf(leaving, next);
        MoveStrip(leaving, next);
        Refresh(next);
        RefreshSoon(next);
        SaveHidden();
        Changed?.Invoke();
    }

    public void ActivateTab(IntPtr hwnd)
    {
        var g = groups.GroupOf(hwnd);
        Log.Write($"activate {hwnd} group={g?.Id} active={g?.Active}");
        if (g is null) { Native.Activate(hwnd); return; }
        var prev = g.Active;
        if (prev != hwnd)
        {
            var f = Native.Frame(prev);
            bool visible = Native.IsWindowVisible(prev) && !Native.IsIconic(prev);
            Crossfade.Run(new Rectangle(f.Left, f.Top, f.Width, f.Height), Settings.SmoothSwitch && visible, () =>
            {
                groups.Activate(hwnd);
                ShowInPlaceOf(prev, hwnd);
                MoveStrip(prev, hwnd);
                HideMember(prev);
            });
        }
        Native.Activate(hwnd);
        Refresh(hwnd);
        RefreshSoon(hwnd);
        SaveHidden();
        Changed?.Invoke();
    }

    /// <summary>Drop window <paramref name="w"/> into the group of shown window <paramref name="target"/>, at tab <paramref name="at"/>.</summary>
    public bool GroupInto(IntPtr w, IntPtr target, int? at = null)
    {
        if (!Rules.CanJoin(Settings, WindowInfo.Facts(w), WindowInfo.Facts(target))) return false;
        var old = groups.GroupOf(w);
        bool sameGroup = old is not null && old.Members.Contains(target);
        var before = old?.Members.ToList() ?? new List<IntPtr>();
        bool wasActive = old?.Active == w;
        var frameOwner = groups.GroupOf(target)?.Active ?? target;
        if (!groups.Join(w, target, at)) return false;
        if (sameGroup) { ActivateTab(w); return true; }
        if (wasActive) HandOff(w, before);
        ShowInPlaceOf(frameOwner, w);
        // The dragged window keeps its own strip (it is handling this drop); otherwise the target's strip moves over.
        if (!strips.ContainsKey(w)) MoveStrip(frameOwner, w);
        foreach (var m in groups.TabsFor(w)) if (m != w && Native.IsWindowVisible(m)) HideMember(m);
        Native.Activate(w);
        Refresh(w);
        RefreshSoon(w);
        SaveHidden();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Take a window out of its group; it reappears at <paramref name="at"/> (screen point) or a little offset.</summary>
    public void Detach(IntPtr w, Point? at = null)
    {
        var g = groups.GroupOf(w);
        if (g is null) return;
        var before = g.Members.ToList();
        bool wasActive = g.Active == w;
        groups.Detach(w);
        if (wasActive) HandOff(w, before);
        if (!Native.IsWindowVisible(w)) Show(w);
        var f = Native.Frame(w);
        var p = at ?? new Point(f.Left + 40, f.Top + 40);
        if (Native.IsZoomed(w)) Native.ShowWindow(w, Native.SW_RESTORE);
        Native.SetWindowPos(w, IntPtr.Zero, p.X - 60, p.Y - 10, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
        Native.Activate(w);
        Refresh(w);
        foreach (var m in before) if (m != w) Refresh(m);
        SaveHidden();
        Changed?.Invoke();
    }

    public void UngroupAll()
    {
        foreach (var g in groups.Groups.ToList())
            foreach (var m in g.Members.Skip(1).ToList()) Detach(m);
    }

    private void AutoGroup(IntPtr hwnd)
    {
        if (!WindowInfo.Eligible(hwnd)) return;
        var others = strips.Keys.Where(k => k != hwnd && Native.IsWindowVisible(k)).Select(WindowInfo.Facts);
        if (Rules.AutoGroupTarget(Settings, WindowInfo.Facts(hwnd), others) is { } t) GroupInto(hwnd, t);
    }

    // ------------------------------------------------------------------ drag and drop

    public void BeginDrag(IntPtr tab, TabStrip from)
    {
        dragging = tab;
        ghost = new DragGhost(Native.Title(tab), IconOf(tab), from.UiScale, Palette.For(Settings).On);
        ghost.MoveTo(Cursor.Position);
        ghost.Show();
    }

    public void DragMove(Point screen)
    {
        ghost?.MoveTo(screen);
        var t = StripUnder(screen);
        if (t != dropStrip)
        {
            if (dropStrip is not null) { dropStrip.DropHighlight = false; dropStrip.Reposition(); }
            dropStrip = t;
            if (t is not null) { t.DropHighlight = true; t.Reposition(); }
        }
    }

    private TabStrip? StripUnder(Point screen)
    {
        var root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = screen.X, Y = screen.Y }), Native.GA_ROOT);
        return strips.Values.FirstOrDefault(s => s.IsHandleCreated && s.Handle == root);
    }

    /// <summary>Drop: onto tabs or a title bar groups; onto the desktop takes it out of its group (or moves a single window).</summary>
    public void Drop(IntPtr tab, Point screen)
    {
        ghost?.Close();
        ghost?.Dispose();
        ghost = null;
        if (dropStrip is not null) { dropStrip.DropHighlight = false; dropStrip.Reposition(); dropStrip = null; }
        dragging = IntPtr.Zero;
        var strip = StripUnder(screen);
        if (strip is not null)
        {
            if (strip.Target == tab && groups.GroupOf(tab) is null) return;
            if (!GroupInto(tab, strip.Target, strip.IndexAt(screen))) Tray.Notify("GrpTabs", "That window cannot be grouped: its app is in GrpTabs exceptions.");
            return;
        }
        var root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = screen.X, Y = screen.Y }), Native.GA_ROOT);
        if (root != IntPtr.Zero && root != tab && strips.ContainsKey(root) && !groups.TabsFor(root).Contains(tab))
        {
            var f = Native.Frame(root);
            if (screen.Y <= f.Top + (int)(48 * WindowInfo.Scale(root)))
            {
                if (!GroupInto(tab, root)) Tray.Notify("GrpTabs", "That window cannot be grouped: its app is in GrpTabs exceptions.");
                return;
            }
        }
        if (groups.GroupOf(tab) is not null) { if (Settings.DragOutDetaches) Detach(tab, screen); }
        else
        {
            if (Native.IsZoomed(tab)) Native.ShowWindow(tab, Native.SW_RESTORE);
            Native.SetWindowPos(tab, IntPtr.Zero, screen.X - 60, screen.Y - 10, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
        }
    }

    /// <summary>Ask a window to close, as its own X button does (it may ask to save).</summary>
    public void CloseTab(IntPtr hwnd) => Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Switch the group shown in <paramref name="target"/>'s frame one tab along (mouse wheel).</summary>
    public void CycleGroup(IntPtr target, int dir)
    {
        if (groups.Cycle(target, dir) is { } next) ActivateTab(next);
    }

    /// <summary>Move a tab one place left or right in its group.</summary>
    private void MoveTab(IntPtr tab, int dir)
    {
        var g = groups.GroupOf(tab);
        if (g is null) return;
        int i = g.Members.IndexOf(tab), j = i + dir;
        if (j < 0 || j >= g.Members.Count) return;
        (g.Members[i], g.Members[j]) = (g.Members[j], g.Members[i]);
        Refresh(g.Active);
    }

    /// <summary>Pull every other open window of the same program into this window's group.</summary>
    private void GroupAllOfApp(IntPtr target)
    {
        var exe = WindowInfo.Exe(target);
        if (exe.Length == 0) return;
        foreach (var w in WindowInfo.TopLevel())
            if (w != target && !groups.TabsFor(target).Contains(w) && string.Equals(WindowInfo.Exe(w), exe, StringComparison.OrdinalIgnoreCase))
                GroupInto(w, groups.GroupOf(target)?.Active ?? target);
    }

    public void ShowTabMenu(TabStrip strip, IntPtr tab, Point screen)
    {
        var members = groups.TabsFor(strip.Target);
        int index = members.ToList().IndexOf(tab);
        string app = Path.GetFileNameWithoutExtension(WindowInfo.Exe(tab));
        var menu = new ContextMenuStrip();
        menu.Items.Add("Close tab", null, (_, _) => CloseTab(tab));
        menu.Items.Add("Close other tabs", null, (_, _) => { foreach (var m in members.ToList()) if (m != tab) CloseTab(m); }).Enabled = members.Count > 1;
        menu.Items.Add("Close group", null, (_, _) => { foreach (var m in members.ToList()) CloseTab(m); }).Enabled = members.Count > 1;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move tab left", null, (_, _) => MoveTab(tab, -1)).Enabled = index > 0;
        menu.Items.Add("Move tab right", null, (_, _) => MoveTab(tab, 1)).Enabled = index >= 0 && index < members.Count - 1;
        menu.Items.Add(app.Length > 0 ? $"Group all {app} windows here" : "Group all windows of this app here", null, (_, _) => GroupAllOfApp(strip.Target));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Take out of group", null, (_, _) => Detach(tab)).Enabled = members.Count > 1;
        menu.Items.Add("Ungroup all tabs here", null, (_, _) => { foreach (var m in members.Skip(1).ToList()) Detach(m); }).Enabled = members.Count > 1;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("GrpTabs settings...", null, (_, _) => Tray.OpenSettings());
        menu.Closed += (_, _) => BeginInvokeDispose(menu);
        menu.Show(screen);
    }

    private static void BeginInvokeDispose(ContextMenuStrip m) => m.BeginInvoke(() => m.Dispose());

    // ------------------------------------------------------------------ hotkeys, pause

    private void OnHotkey(HotkeyAction act)
    {
        if (act == HotkeyAction.Pause) { SetPaused(!Paused); return; }
        if (Paused) return;
        var fg = Native.GetForegroundWindow();
        if (act == HotkeyAction.Detach) { if (groups.GroupOf(fg) is not null) Detach(fg); return; }
        if (groups.Cycle(fg, act == HotkeyAction.Next ? 1 : -1) is { } next) ActivateTab(next);
    }

    /// <summary>Ctrl+Alt+1 to 9: the tab with that number in the active window's group.</summary>
    private void OnNumberHotkey(int n)
    {
        if (Paused) return;
        var g = groups.GroupOf(Native.GetForegroundWindow());
        if (g is not null && n >= 1 && n <= g.Members.Count) ActivateTab(g.Members[n - 1]);
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        hotkeys.Register(Settings, paused);
        foreach (var w in strips.Keys.ToList()) Refresh(w);
        if (!paused) foreach (var w in WindowInfo.TopLevel()) Refresh(w);
        Tray.Notify("GrpTabs", paused ? "Paused: tabs and hotkeys are off until you resume." : "Resumed.");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ never lose a window

    private void SaveHidden()
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            var list = hiddenByUs.Where(Native.IsWindow).Select(h => new HiddenEntry(h.ToInt64(), WindowInfo.Pid(h), Native.Title(h))).ToList();
            File.WriteAllText(HiddenFile, JsonSerializer.Serialize(list));
        }
        catch (IOException) { /* best effort: the in-memory list still restores them on exit */ }
    }

    private record HiddenEntry(long Hwnd, uint Pid, string Title);

    /// <summary>After a crash or a forced stop, show any window GrpTabs had hidden that still exists.</summary>
    private static void RecoverHidden()
    {
        try
        {
            if (!File.Exists(HiddenFile)) return;
            var list = JsonSerializer.Deserialize<List<HiddenEntry>>(File.ReadAllText(HiddenFile)) ?? new();
            foreach (var e in list)
            {
                var h = new IntPtr(e.Hwnd);
                if (Native.IsWindow(h) && WindowInfo.Pid(h) == e.Pid && !Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWNA);
            }
            File.Delete(HiddenFile);
        }
        catch (Exception) { /* nothing to recover */ }
    }

    /// <summary>Show every hidden window again (exit, update, uninstall).</summary>
    public void RestoreAll()
    {
        foreach (var h in hiddenByUs.ToList())
            if (Native.IsWindow(h)) Native.ShowWindow(h, Native.SW_SHOWNA);
        hiddenByUs.Clear();
        try { File.Delete(HiddenFile); } catch (IOException) { }
    }

    public void Dispose()
    {
        hoverTimer.Stop();
        foreach (var h in hooks) Native.UnhookWinEvent(h);
        hooks.Clear();
        RestoreAll();
        foreach (var s in strips.Values.ToList()) { s.Close(); s.Dispose(); }
        strips.Clear();
        hotkeys.Dispose();
    }
}

/// <summary>Receives WM_HOTKEY for the registered global hotkeys.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action<HotkeyAction> onHotkey;
    private readonly Action<int> onNumber;
    private readonly List<int> ids = new();
    public List<string> Failures { get; } = new();

    public HotkeyWindow(Action<HotkeyAction> onHotkey, Action<int> onNumber)
    {
        this.onHotkey = onHotkey;
        this.onNumber = onNumber;
        CreateHandle(new CreateParams { Caption = "GrpTabs hotkeys" });
    }

    /// <summary>Registers the hotkeys; while paused only the pause hotkey. Keys another app already owns are listed in Failures.</summary>
    public void Register(Settings st, bool paused)
    {
        foreach (var id in ids) Native.UnregisterHotKey(Handle, id);
        ids.Clear();
        Failures.Clear();
        foreach (var (act, text) in st.Hotkeys)
        {
            if (paused && act != HotkeyAction.Pause) continue;
            if (Hotkey.Parse(text) is not { } hk) continue;
            int id = 0xB000 + (int)act;
            if (Native.RegisterHotKey(Handle, id, hk.Modifiers, (uint)hk.Key)) ids.Add(id);
            else Failures.Add($"{hk} ({Hotkey.Labels[act]})");
        }
        if (paused || !st.NumberHotkeys) return;
        int taken = 0;
        for (int n = 1; n <= 9; n++)
        {
            int id = 0xB100 + n;
            if (Native.RegisterHotKey(Handle, id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)(Keys.D0 + n))) ids.Add(id);
            else taken++;
        }
        if (taken > 0) Failures.Add($"Ctrl+Alt+number for {taken} of the 9 tabs");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() is int id and >= 0xB000 and < 0xB010) onHotkey((HotkeyAction)(id - 0xB000));
        else if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() is int nid and > 0xB100 and <= 0xB109) onNumber(nid - 0xB100);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in ids) Native.UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}
