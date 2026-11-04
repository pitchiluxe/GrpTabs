using System.Drawing.Drawing2D;
using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// GrpTabs settings: General, Appearance, Behavior, Exceptions, Groups and
/// Hotkeys. Changes go to a pending copy; Apply or OK saves them, Cancel
/// drops them. Every control changes something real.
/// </summary>
internal sealed class SettingsForm : Form
{
    private enum Page { General, Appearance, Behavior, Exceptions, Groups, Hotkeys }

    private readonly Updater updater;
    private Settings pending;
    private bool pendingStartup;
    private Page page = Page.General;
    private readonly Panel nav = new() { Dock = DockStyle.Left, Width = 176, BackColor = Color.White, Padding = new Padding(0, 10, 0, 0) };
    private readonly Panel content = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 16, 20, 16), BackColor = Palette.Paper };
    private readonly Button okBtn = Btn("OK", primary: true), cancelBtn = Btn("Cancel"), applyBtn = Btn("Apply");
    private string updateStatus = "";

    public SettingsForm(Updater updater)
    {
        this.updater = updater;
        pending = Tray.CurrentSettings.Clone();
        pendingStartup = SafeStartup();
        Text = "GrpTabs Settings";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 620);
        MinimumSize = new Size(700, 560);
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Palette.Paper;
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new HeaderPanel(updater.CurrentVersion) { Dock = DockStyle.Top, Height = 92 };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Palette.IndigoDeep, Padding = new Padding(18, 13, 16, 13) };
        var credit = new Label { Text = "Created by Erick Omari", ForeColor = Color.FromArgb(199, 210, 254), AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 8, 0, 0), Font = new Font("Segoe UI Semibold", 9.75f) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        buttons.Controls.AddRange(new Control[] { okBtn, cancelBtn, applyBtn });
        footer.Controls.Add(buttons);
        footer.Controls.Add(credit);
        okBtn.Click += (_, _) => { if (Apply()) Close(); };
        cancelBtn.Click += (_, _) => Close();
        applyBtn.Click += (_, _) => Apply();
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        Controls.Add(content);
        Controls.Add(nav);
        Controls.Add(footer);
        Controls.Add(header);
        BuildNav();
        Draw();
        if (Tray.CurrentEngine is { } eng) eng.Changed += OnEngineChanged;
        updater.StateChanged += OnEngineChanged;
    }

    /// <summary>WS_EX_COMPOSITED: Windows paints the window and all its controls off-screen in one pass, so hovering never flickers.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;
            return cp;
        }
    }

    private static bool SafeStartup() { try { return Settings.StartsWithWindows; } catch (Exception) { return false; } }

    private void OnEngineChanged()
    {
        if (IsDisposed) return;
        BeginInvoke(() => { if (page is Page.General or Page.Groups) Draw(); });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (Tray.CurrentEngine is { } eng) eng.Changed -= OnEngineChanged;
        updater.StateChanged -= OnEngineChanged;
        base.OnFormClosed(e);
    }

    private bool Dirty => pending.ToJson() != Tray.CurrentSettings.ToJson() || pendingStartup != SafeStartup();

    private bool Apply()
    {
        var clash = Hotkey.Clashes(pending.Hotkeys);
        if (clash.Count > 0)
        {
            page = Page.Hotkeys;
            BuildNav();
            Draw();
            MessageBox.Show(this, $"Two actions share {pending.Hotkeys[clash[0].Item1]}. Give each its own hotkey.", "GrpTabs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try { if (pendingStartup != SafeStartup()) Settings.StartsWithWindows = pendingStartup; }
        catch (Exception ex) { MessageBox.Show(this, $"Could not change Start with Windows: {ex.Message}", "GrpTabs", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        Tray.ApplySettings(pending.Clone());
        pending = Tray.CurrentSettings.Clone();
        Draw();
        return true;
    }

    private void Changed()
    {
        applyBtn.Enabled = Dirty;
    }

    // ------------------------------------------------------------------ layout helpers

    private static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 34), FlatStyle = FlatStyle.Flat, Margin = new Padding(6, 0, 0, 0), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? Palette.Indigo : Color.FromArgb(198, 201, 218);
        b.BackColor = primary ? Palette.Indigo : Color.White;
        b.ForeColor = primary ? Color.White : Palette.Ink;
        return b;
    }

    private void BuildNav()
    {
        nav.Controls.Clear();
        foreach (var p in Enum.GetValues<Page>().Reverse())
        {
            var b = new NavButton(p.ToString().ToUpperInvariant(), p == page) { Dock = DockStyle.Top, Height = 44 };
            b.Click += (_, _) => { page = p; BuildNav(); Draw(); };
            nav.Controls.Add(b);
        }
    }

    private FlowLayoutPanel stack = new();

    private void Draw()
    {
        content.SuspendLayout();
        content.Controls.Clear();
        stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent };
        content.Controls.Add(stack);
        switch (page)
        {
            case Page.General: General(); break;
            case Page.Appearance: Appearance(); break;
            case Page.Behavior: Behavior(); break;
            case Page.Exceptions: Exceptions(); break;
            case Page.Groups: GroupsPage(); break;
            case Page.Hotkeys: Hotkeys(); break;
        }
        content.ResumeLayout();
        Changed();
    }

    private int CardWidth => Math.Max(420, content.ClientSize.Width - content.Padding.Horizontal - 6);

    private Card Card(string title)
    {
        var c = new Card(title) { Width = CardWidth };
        stack.Controls.Add(c);
        return c;
    }

    private static CheckBox Check(string text, bool value, Action<bool> change, string? note = null)
    {
        var cb = new CheckBox { Text = note is null ? text : $"{text}\n{note}", Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4), ForeColor = Palette.Ink, Cursor = Cursors.Hand };
        cb.CheckedChanged += (_, _) => change(cb.Checked);
        return cb;
    }

    private static Label Note(string text, Color? color = null) => new() { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = color ?? Color.FromArgb(90, 94, 117), Margin = new Padding(0, 2, 0, 6) };

    private static FlowLayoutPanel Row(params Control[] items)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 4), BackColor = Color.Transparent };
        foreach (var i in items) { i.Margin = new Padding(0, 0, 8, 0); r.Controls.Add(i); }
        return r;
    }

    private Control Radios<T>(string group, T value, (T Val, string Text)[] opts, Action<T> change) where T : struct, Enum
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Color.Transparent };
        foreach (var (v, t) in opts)
        {
            var rb = new RadioButton { Text = t, Checked = v.Equals(value), AutoSize = true, ForeColor = Palette.Ink, Margin = new Padding(0, 3, 0, 3), Cursor = Cursors.Hand };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { change(v); Changed(); } };
            panel.Controls.Add(rb);
        }
        return panel;
    }

    private Control Slider(string label, int value, int min, int max, string unit, Action<int> change)
    {
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Palette.Ink, Margin = new Padding(0, 8, 0, 0), Width = 260 };
        var tb = new TrackBar { Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, Width = 220, SmallChange = 1, LargeChange = 10 };
        var val = new Label { Text = $"{value}{unit}", AutoSize = true, ForeColor = Palette.IndigoDeep, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 0) };
        tb.ValueChanged += (_, _) => { val.Text = $"{tb.Value}{unit}"; change(tb.Value); Changed(); };
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        lbl.AutoSize = false; lbl.Size = new Size(260, 24);
        r.Controls.AddRange(new Control[] { lbl, tb, val });
        return r;
    }

    // ------------------------------------------------------------------ pages

    private void General()
    {
        var c = Card("Settings");
        c.Add(Check("Start with Windows", pendingStartup, v => { pendingStartup = v; Changed(); }, "Listed in Task Manager > Startup apps as GrpTabs."));
        c.Add(Check("Show an icon in the notification area", pending.TrayIcon, v => { pending.TrayIcon = v; Changed(); }));
        c.Add(Note("When the icon is hidden, open these settings by starting GrpTabs again from the Start menu."));

        var u = Card("Updates");
        u.Add(Check("Automatically check for updates", pending.CheckForUpdates, v => { pending.CheckForUpdates = v; Changed(); }, "Checks GitHub at start and every six hours, downloads in the background and notifies you."));
        u.Add(Note($"Installed version {updater.CurrentVersion}. {updateStatus}"));
        var check = Btn("Check for updates now");
        check.Click += async (_, _) => { check.Enabled = false; updateStatus = await updater.CheckAsync(true); Draw(); };
        var restart = Btn($"Restart to update to {updater.ReadyVersion}", primary: true);
        restart.Visible = updater.ReadyVersion is not null;
        restart.Click += (_, _) => updater.RestartToUpdate();
        u.Add(Row(check, restart));

        var eng = Tray.CurrentEngine;
        var s = Card("Status");
        s.Add(Note(eng is null ? "GrpTabs is stopping." : eng.Paused ? "GrpTabs is paused: no tabs, and only the pause hotkey works." : $"GrpTabs is running. {eng.Groups.Count} group{(eng.Groups.Count == 1 ? "" : "s")} open.", Palette.Ink));
        var pause = Btn(eng?.Paused == true ? "Resume" : "Pause");
        pause.Click += (_, _) => { eng?.SetPaused(!eng.Paused); Draw(); };
        var ungroup = Btn("Ungroup all windows");
        ungroup.Enabled = eng?.Groups.Count > 0;
        ungroup.Click += (_, _) => { eng?.UngroupAll(); Draw(); };
        var exit = Btn("Exit GrpTabs");
        exit.Click += (_, _) => { if (MessageBox.Show(this, "Exit GrpTabs? Every grouped window is shown again.", "GrpTabs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Tray.ExitApp(); };
        s.Add(Row(pause, ungroup, exit));

        var reset = Btn("Restore all settings to their default values");
        reset.Width = CardWidth;
        reset.AutoSize = false;
        reset.Height = 38;
        reset.Margin = new Padding(0, 4, 0, 0);
        reset.Click += (_, _) => { pending = new Settings(); pendingStartup = true; Draw(); };
        stack.Controls.Add(reset);
    }

    private void Appearance()
    {
        var c = Card("Tabs");
        c.Add(Slider("Opacity when the pointer is elsewhere", pending.IdleOpacity, 30, 100, "%", v => { pending.IdleOpacity = v; preview?.Invalidate(); }));
        c.Add(Slider("Maximum tab width", pending.TabWidth, 90, 260, " px", v => { pending.TabWidth = v; preview?.Invalidate(); }));
        c.Add(Check("Show app icons on tabs", pending.ShowIcons, v => { pending.ShowIcons = v; preview?.Invalidate(); Changed(); }));
        c.Add(Check("Fade smoothly between windows when switching tabs", pending.SmoothSwitch, v => { pending.SmoothSwitch = v; Changed(); },
            Crossfade.WindowsAnimates() ? null : "Windows animation effects are off, so GrpTabs switches instantly."));
        var colour = Card("Tab colour");
        colour.Add(Note("The active tab takes this colour; the others are a lighter or darker tint of it, with text that stays readable."));
        var swatches = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(CardWidth - 40, 0), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 6) };
        foreach (var (name, hex) in TabColors.Presets)
            swatches.Controls.Add(Swatch(name, TabColors.Parse(hex)!.Value, pending.TabColor == hex || (pending.TabColor == "" && hex == "#4F46E5"), () => SetColour(hex == "#4F46E5" ? "" : hex)));
        var accent = Palette.WindowsAccent();
        swatches.Controls.Add(Swatch(accent is null ? "Windows accent colour (not found)" : "Windows accent colour", accent ?? Color.Gray, pending.TabColor == "accent", () => SetColour("accent"), ring: true));
        colour.Add(swatches);
        var custom = Btn(TabColors.Parse(pending.TabColor) is { } cur && !TabColors.Presets.Any(p => p.Hex == pending.TabColor) ? $"Custom colour {pending.TabColor}..." : "Custom colour...");
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = Palette.BaseColor(pending.TabColor) };
            if (dlg.ShowDialog(this) == DialogResult.OK) SetColour(TabColors.Hex(dlg.Color));
        };
        colour.Add(custom);
        var t = Card("Theme");
        t.Add(Radios("theme", pending.Theme, new[] { (GrpTheme.Auto, "Follow Windows (light or dark mode)"), (GrpTheme.Light, "Light"), (GrpTheme.Dark, "Dark") }, v => { pending.Theme = v; preview?.Invalidate(); }));
        var p = Card("Preview");
        preview = new PreviewPanel(() => pending) { Width = CardWidth - 34, Height = 110 };
        p.Add(preview);
    }

    private PreviewPanel? preview;

    private void SetColour(string value)
    {
        pending.TabColor = TabColors.Normalize(value);
        Draw();
    }

    private static Control Swatch(string name, Color color, bool selected, Action pick, bool ring = false)
    {
        var b = new SwatchButton(color, selected, ring) { Size = new Size(36, 36), Margin = new Padding(0, 0, 8, 8), AccessibleName = name };
        new ToolTip().SetToolTip(b, name);
        b.Click += (_, _) => pick();
        return b;
    }

    private void Behavior()
    {
        var when = new[] { (TabsWhen.Always, "Always"), (TabsWhen.Hover, "When the pointer is over the window"), (TabsWhen.Never, "Never") };
        Card("Show tabs on windows in a group").Add(Radios("grouped", pending.GroupedTabs, when, v => pending.GroupedTabs = v));
        var single = Card("Show tabs on a window on its own");
        single.Add(Radios("single", pending.SingleTabs, when, v => pending.SingleTabs = v));
        single.Add(Note("A window needs its tab to be dragged into a group. \"Never\" leaves only automatic grouping."));
        var c = Card("Tabs");
        c.Add(Check("Hide tabs on maximized windows", pending.HideWhenMaximized, v => { pending.HideWhenMaximized = v; Changed(); }));
        c.Add(Check("Middle-click a tab to close its window", pending.MiddleClickCloses, v => { pending.MiddleClickCloses = v; Changed(); }));
        c.Add(Check("Double-click a tab to maximize or restore", pending.DoubleClickMaximizes, v => { pending.DoubleClickMaximizes = v; Changed(); }));
        c.Add(Check("Drag a tab out of its group onto the desktop to take it out", pending.DragOutDetaches, v => { pending.DragOutDetaches = v; Changed(); }));
        c.Add(Check("Show a close button on tabs", pending.CloseButtons, v => { pending.CloseButtons = v; Changed(); }, "On the active tab and the one under the pointer."));
        c.Add(Check("Scroll the mouse wheel over the tabs to switch", pending.WheelSwitches, v => { pending.WheelSwitches = v; Changed(); }));
        c.Add(Check("Hide tabs over full-screen windows", pending.HideInFullScreen, v => { pending.HideInFullScreen = v; Changed(); }, "Games, videos and presentations that cover the whole screen."));
    }

    /// <summary>Programs with a window open now, plus any already in the list.</summary>
    private static List<string> Programs(IEnumerable<string> extra)
    {
        var list = WindowInfo.TopLevel().Select(WindowInfo.Exe).Where(e => e.Length > 0).Concat(extra);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Control ProgramList(List<string> selected, string addLabel)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Color.Transparent };
        var list = new CheckedListBox { Width = CardWidth - 40, Height = 220, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        foreach (var p in Programs(selected)) list.Items.Add(p, selected.Contains(p, StringComparer.OrdinalIgnoreCase));
        list.ItemCheck += (_, e) =>
        {
            var name = (string)list.Items[e.Index];
            selected.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
            if (e.NewValue == CheckState.Checked) selected.Add(name);
            BeginInvoke(Changed);
        };
        var box = new TextBox { Width = 260, PlaceholderText = "Program file name, e.g. notepad.exe" };
        var add = Btn(addLabel);
        add.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length == 0) return;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            if (!selected.Contains(name, StringComparer.OrdinalIgnoreCase)) selected.Add(name);
            Draw();
        };
        panel.Controls.Add(list);
        panel.Controls.Add(Row(box, add));
        return panel;
    }

    private void Exceptions()
    {
        var c = Card("Never show tabs on these programs");
        c.Add(Note("Their windows get no tabs, cannot be dragged into a group, and nothing can be grouped with them. Programs with a window open now are listed."));
        c.Add(ProgramList(pending.Exceptions, "Add program"));
    }

    private void GroupsPage()
    {
        var a = Card("Group automatically");
        a.Add(Note("A new window of a ticked program joins the group of an open window of the same program."));
        a.Add(ProgramList(pending.AutoGroup, "Add program"));

        var o = Card("Open groups");
        var eng = Tray.CurrentEngine;
        if (eng is null || eng.Groups.Count == 0) { o.Add(Note("No groups yet. Drag a window's tab onto another window's tabs or title bar.")); return; }
        int n = 0;
        foreach (var g in eng.Groups.ToList())
        {
            o.Add(new Label { Text = $"Group {++n}", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Palette.IndigoDeep, Margin = new Padding(0, 6, 0, 2) });
            foreach (var m in g.Members.ToList())
            {
                var title = new LinkLabel { Text = Native.Title(m) + (m == g.Active ? "   (showing)" : ""), AutoSize = true, LinkColor = Palette.Ink, MaximumSize = new Size(440, 0) };
                title.LinkClicked += (_, _) => eng.ActivateTab(m);
                var take = new LinkLabel { Text = "Take out", AutoSize = true, LinkColor = Palette.Indigo };
                take.LinkClicked += (_, _) => { eng.Detach(m); Draw(); };
                o.Add(Row(title, take));
            }
        }
        var all = Btn("Ungroup all windows");
        all.Click += (_, _) => { eng.UngroupAll(); Draw(); };
        o.Add(all);
    }

    private void Hotkeys()
    {
        var c = Card("Hotkeys");
        c.Add(Note("Click a box and press the keys, with Ctrl or Alt. Backspace clears it (no hotkey)."));
        c.Add(Check("Ctrl+Alt+1 to 9 jump to that tab of the active group", pending.NumberHotkeys, v => { pending.NumberHotkeys = v; Changed(); }));
        var clashes = Hotkey.Clashes(pending.Hotkeys);
        foreach (var act in Enum.GetValues<HotkeyAction>())
        {
            var label = new Label { Text = Hotkey.Labels[act], AutoSize = false, Size = new Size(300, 28), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Palette.Ink };
            bool clash = clashes.Any(x => x.Item1 == act || x.Item2 == act);
            var box = new TextBox { ReadOnly = true, Width = 220, Text = string.IsNullOrEmpty(pending.Hotkeys[act]) ? "None" : pending.Hotkeys[act], BackColor = clash ? Color.FromArgb(254, 228, 226) : Color.FromArgb(248, 249, 254), Font = new Font("Consolas", 10.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            box.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (e.KeyCode is Keys.Back or Keys.Delete) { pending.Hotkeys[act] = ""; Draw(); return; }
                if (Hotkey.FromKeys(e.KeyData) is { } hk) { pending.Hotkeys[act] = hk.ToString(); Draw(); }
            };
            c.Add(Row(label, box));
        }
        if (clashes.Count > 0) c.Add(Note("Two actions share a hotkey. Change one before you apply.", Color.FromArgb(180, 35, 24)));
        if (Tray.CurrentEngine?.HotkeyFailures is { Count: > 0 } f) c.Add(Note($"Another app already uses: {string.Join(", ", f)}.", Color.FromArgb(180, 35, 24)));
    }
}

/// <summary>A white card with a title, holding a page's controls.</summary>
internal sealed class Card : Panel
{
    private readonly FlowLayoutPanel body;

    public Card(string title)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 12);
        Margin = new Padding(0, 0, 0, 14);
        body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.White };
        body.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Palette.IndigoDeep, Margin = new Padding(0, 0, 0, 6) });
        Controls.Add(body);
    }

    public void Add(Control c) => body.Controls.Add(c);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(207, 210, 227));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using var bar = new SolidBrush(Palette.Indigo);
        e.Graphics.FillRectangle(bar, 0, 0, 3, Height);
    }
}

/// <summary>A left navigation entry: indigo bar and tint when selected.</summary>
internal sealed class NavButton : Control
{
    private readonly bool selected;
    private bool hover;

    public NavButton(string text, bool selected)
    {
        Text = text;
        this.selected = selected;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Font = new Font("Segoe UI Semibold", 9.75f);
        AccessibleRole = AccessibleRole.PageTab;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(e); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(selected ? Palette.IndigoSoft : hover ? Color.FromArgb(238, 240, 251) : Color.White);
        if (selected) { using var b = new SolidBrush(Palette.Indigo); g.FillRectangle(b, 0, 0, 4, Height); }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(20, 0, Width - 20, Height), selected ? Palette.IndigoDeep : Color.FromArgb(107, 111, 133), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(6, 6, Width - 12, Height - 12));
    }
}

/// <summary>The banner: logo, "GrpTabs" and the version.</summary>
internal sealed class HeaderPanel : Panel
{
    private readonly string version;
    private readonly Image? logo = AppIcon.Logo();

    public HeaderPanel(string version)
    {
        this.version = version;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(ClientRectangle, Palette.IndigoDeep, Color.FromArgb(99, 102, 241), 10f)) g.FillRectangle(bg, ClientRectangle);
        int s = (int)(Height * 0.62);
        if (logo is not null) g.DrawImage(logo, 22, (Height - s) / 2, s, s);
        int x = 22 + s + 14;
        using var light = new Font("Segoe UI Light", 26f);
        using var bold = new Font("Segoe UI", 26f, FontStyle.Bold);
        var grp = TextRenderer.MeasureText(g, "Grp", light, Size.Empty, TextFormatFlags.NoPadding);
        int y = (Height - grp.Height) / 2 - 2;
        TextRenderer.DrawText(g, "Grp", light, new Point(x, y), Color.White, TextFormatFlags.NoPadding);
        var tabs = TextRenderer.MeasureText(g, "Tabs", bold, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Tabs", bold, new Point(x + grp.Width, y), Color.White, TextFormatFlags.NoPadding);
        using var small = new Font("Segoe UI", 9f);
        TextRenderer.DrawText(g, $"v{version}", small, new Point(x + grp.Width + tabs.Width + 8, y + grp.Height - 22), Color.FromArgb(199, 210, 254));
    }
}

/// <summary>Appearance preview: three tabs over a window, drawn with the pending settings.</summary>
internal sealed class PreviewPanel : Panel
{
    private readonly Func<Settings> st;

    public PreviewPanel(Func<Settings> st)
    {
        this.st = st;
        DoubleBuffered = true;
        Margin = new Padding(0, 4, 0, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var s = st();
        var pal = Palette.For(s);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(230, 233, 242), Color.FromArgb(240, 242, 248))) g.FillRectangle(hatch, ClientRectangle);
        using (var win = new SolidBrush(Color.White)) g.FillRectangle(win, 14, 40, Width - 28, Height - 40);
        using (var border = new Pen(Color.FromArgb(198, 201, 218))) g.DrawRectangle(border, 14, 40, Width - 29, Height - 41);
        int alpha = (int)(255 * s.IdleOpacity / 100.0);
        string[] titles = { "Notepad", "Windows PowerShell", "Active Directory Users and Computers" };
        int x = 26;
        using var font = new Font("Segoe UI", 9f);
        for (int i = 0; i < titles.Length; i++)
        {
            bool on = i == 0;
            int w = Math.Min(s.TabWidth, TextRenderer.MeasureText(titles[i], font).Width + (s.ShowIcons ? 40 : 22));
            var r = new Rectangle(x, on ? 12 : 14, w, on ? 28 : 26);
            using var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, 16, 16, 180, 90);
            path.AddArc(r.Right - 16, r.Y, 16, 16, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            path.CloseFigure();
            using (var b = new SolidBrush(Color.FromArgb(alpha, on ? pal.On : pal.Tab))) g.FillPath(b, path);
            using (var edge = new Pen(Color.FromArgb(alpha, on ? pal.On : pal.Line))) g.DrawPath(edge, path);
            int tx = r.X + 10;
            if (s.ShowIcons) { using var ib = new SolidBrush(on ? pal.OnInk : pal.On); g.FillRectangle(ib, tx, r.Y + r.Height / 2 - 7, 14, 14); tx += 20; }
            int right = r.Right - 4 - (s.CloseButtons && on ? 20 : 0);
            TextRenderer.DrawText(g, titles[i], font, new Rectangle(tx, r.Y, right - tx, r.Height), on ? pal.OnInk : pal.TabInk, on ? pal.On : pal.Tab, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (s.CloseButtons && on)
            {
                using var pen = new Pen(pal.OnInk, 1.4f);
                int cx = r.Right - 16, cy = r.Y + r.Height / 2;
                g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
            }
            x += w + 3;
        }
    }
}

/// <summary>A round colour swatch: a ring marks the chosen one; the Windows accent swatch has a white inner ring.</summary>
internal sealed class SwatchButton : Button
{
    private readonly Color color;
    private readonly bool selected, accent;

    public SwatchButton(Color color, bool selected, bool accent)
    {
        this.color = color;
        this.selected = selected;
        this.accent = accent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.White;
        FlatAppearance.MouseDownBackColor = Color.White;
        BackColor = Color.White;
        Cursor = Cursors.Hand;
        Text = "";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 4, Width - 9, Height - 9);
        if (selected) { using var ring = new Pen(color, 2.4f); g.DrawEllipse(ring, 1, 1, Width - 3, Height - 3); }
        using (var fill = new SolidBrush(color)) g.FillEllipse(fill, r);
        if (accent) { using var inner = new Pen(Color.White, 2f); g.DrawEllipse(inner, r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10); }
        if (selected)
        {
            using var tick = new Pen(GrpTabs.Core.TabColors.InkOn(color), 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLines(tick, new[] { new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 6, cy - 4) });
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
    }
}
