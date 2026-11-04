using System.Drawing.Drawing2D;
using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// The tabs above one window. It is a borderless window owned by that window,
/// so Windows keeps it just above it and hides it with it; it never takes the
/// focus. Clicks, drags, middle-click, double-click and the menu go to the engine.
/// </summary>
internal sealed class TabStrip : Form
{
    private static readonly Color Key = Color.FromArgb(255, 0, 255);
    private readonly Engine engine;
    private readonly List<(IntPtr Hwnd, Rectangle Box)> layout = new();
    private Point downAt;
    private IntPtr downTab;
    private bool dragging;
    private bool hot;

    public IntPtr Target { get; private set; }
    public IReadOnlyList<IntPtr> Tabs { get; private set; } = Array.Empty<IntPtr>();
    public float UiScale { get; private set; } = 1f;
    public bool DropHighlight { get; set; }

    public TabStrip(Engine engine, IntPtr target)
    {
        this.engine = engine;
        Target = target;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = "GrpTabs";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, Target);
    }

    /// <summary>Follow another window (the group's active tab changed).</summary>
    public void Retarget(IntPtr target)
    {
        if (Target == target) return;
        Target = target;
        if (IsHandleCreated) Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, target);
    }

    public void SetTabs(IReadOnlyList<IntPtr> tabs)
    {
        Tabs = tabs.ToList();
        Invalidate();
    }

    /// <summary>Above the window's top edge, or over its title bar when there is no room (maximized windows).</summary>
    public void Reposition()
    {
        var st = engine.Settings;
        UiScale = WindowInfo.Scale(Target);
        var f = Native.Frame(Target);
        int h = (int)(28 * UiScale);
        using var g = CreateGraphics();
        LayoutTabs(g, st, h);
        int width = layout.Count > 0 ? layout[^1].Box.Right + 2 : 10;
        int maxWidth = Math.Max((int)(120 * UiScale), f.Width - (int)(150 * UiScale));
        width = Math.Min(width, maxWidth);
        var work = Screen.FromHandle(Target).WorkingArea;
        int top = f.Top - h;
        int left = f.Left + (int)(12 * UiScale);
        if (top < work.Top) { top = f.Top; left = f.Left + (int)(46 * UiScale); }
        SetBounds(left, top, width, h);
        Opacity = hot || DropHighlight ? 1 : st.IdleOpacity / 100.0;
        Invalidate();
    }

    private void LayoutTabs(Graphics g, Settings st, int h)
    {
        layout.Clear();
        int x = 0;
        int maxW = (int)(st.TabWidth * UiScale), minW = (int)(70 * UiScale);
        foreach (var hwnd in Tabs)
        {
            string title = Native.Title(hwnd);
            int text = (int)Math.Ceiling(g.MeasureString(title, TabFont).Width);
            int w = Math.Clamp(text + (int)((st.ShowIcons ? 40 : 22) * UiScale), minW, maxW);
            layout.Add((hwnd, new Rectangle(x, 0, w, h)));
            x += w + (int)(2 * UiScale);
        }
    }

    private Font? font;
    private float fontScale;
    private Font TabFont
    {
        get
        {
            if (font is null || Math.Abs(fontScale - UiScale) > 0.01f) { font?.Dispose(); font = new Font("Segoe UI", 9f * UiScale, GraphicsUnit.Point); fontScale = UiScale; }
            return font;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var st = engine.Settings;
        var pal = Palette.For(st.Theme);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        LayoutTabs(g, st, Height);
        var active = engine.ActiveOf(Target);
        int r = (int)(8 * UiScale);
        foreach (var (hwnd, box) in layout)
        {
            if (box.Left >= Width) break;
            bool on = hwnd == active;
            var rect = new Rectangle(box.X, box.Y + (on ? 0 : (int)(2 * UiScale)), Math.Min(box.Width, Width - box.X - 1), box.Height - (on ? 0 : (int)(2 * UiScale)));
            using var path = Rounded(rect, r);
            using (var fill = new SolidBrush(on ? pal.On : (hwnd == hoverTab ? pal.Hover : pal.Tab))) g.FillPath(fill, path);
            using (var pen = new Pen(on ? pal.On : pal.Line, Math.Max(1f, UiScale))) g.DrawPath(pen, path);
            int pad = (int)(10 * UiScale);
            int textLeft = rect.X + pad;
            if (st.ShowIcons)
            {
                var icon = engine.IconOf(hwnd);
                int sz = (int)(16 * UiScale);
                if (icon is not null) g.DrawImage(icon, rect.X + pad, rect.Y + (rect.Height - sz) / 2, sz, sz);
                textLeft += sz + (int)(6 * UiScale);
            }
            var textRect = new Rectangle(textLeft, rect.Y, rect.Right - textLeft - pad / 2, rect.Height);
            TextRenderer.DrawText(g, Native.Title(hwnd), TabFont, textRect, on ? pal.OnInk : pal.TabInk, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
        if (DropHighlight)
        {
            using var pen = new Pen(Palette.Indigo, 2 * UiScale);
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    public IntPtr TabAt(Point client) => layout.FirstOrDefault(l => l.Box.Contains(client)).Hwnd;

    /// <summary>Where a tab dropped at this screen point goes in the tab order.</summary>
    public int IndexAt(Point screen)
    {
        var p = PointToClient(screen);
        for (int i = 0; i < layout.Count; i++)
            if (p.X < layout[i].Box.Left + layout[i].Box.Width / 2) return i;
        return layout.Count;
    }

    private IntPtr hoverTab;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (downTab != IntPtr.Zero && e.Button == MouseButtons.Left)
        {
            if (!dragging && (Math.Abs(e.X - downAt.X) > 6 || Math.Abs(e.Y - downAt.Y) > 6)) { dragging = true; engine.BeginDrag(downTab, this); }
            if (dragging) engine.DragMove(Cursor.Position);
            return;
        }
        var t = TabAt(e.Location);
        if (t != hoverTab) { hoverTab = t; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Opacity = 1; engine.StripHover(this, true); }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = false;
        hoverTab = IntPtr.Zero;
        Opacity = engine.Settings.IdleOpacity / 100.0;
        engine.StripHover(this, false);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Log.Write($"strip down {e.Button} at {e.Location} tab={TabAt(e.Location)} target={Target}");
        if (e.Button != MouseButtons.Left) return;
        downTab = TabAt(e.Location);
        downAt = e.Location;
        dragging = false;
        Capture = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var tab = TabAt(e.Location);
        Log.Write($"strip up {e.Button} at {e.Location} tab={tab} down={downTab} dragging={dragging}");
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            var was = downTab;
            downTab = IntPtr.Zero;
            if (dragging) { dragging = false; engine.Drop(was, Cursor.Position); }
            else if (was != IntPtr.Zero) engine.ActivateTab(was);
        }
        else if (e.Button == MouseButtons.Middle && tab != IntPtr.Zero && engine.Settings.MiddleClickCloses) Native.PostMessage(tab, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        else if (e.Button == MouseButtons.Right && tab != IntPtr.Zero) engine.ShowTabMenu(this, tab, Cursor.Position);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var tab = TabAt(e.Location);
        if (e.Button == MouseButtons.Left && tab != IntPtr.Zero && engine.Settings.DoubleClickMaximizes)
            Native.ShowWindow(tab, Native.IsZoomed(tab) ? Native.SW_RESTORE : Native.SW_SHOWMAXIMIZED);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) font?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>The tab that follows the pointer while you drag it: see-through to clicks, always on top.</summary>
internal sealed class DragGhost : Form
{
    private readonly string title;
    private readonly Image? icon;
    private readonly float scale;

    public DragGhost(string title, Image? icon, float scale)
    {
        this.title = title;
        this.icon = icon;
        this.scale = scale;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Palette.Indigo;
        Opacity = 0.88;
        Size = new Size((int)(200 * scale), (int)(28 * scale));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) | 0x20 /* WS_EX_TRANSPARENT */ | 0x80000 /* WS_EX_LAYERED */;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int x = (int)(10 * scale);
        int sz = (int)(16 * scale);
        if (icon is not null) { e.Graphics.DrawImage(icon, x, (Height - sz) / 2, sz, sz); x += sz + (int)(6 * scale); }
        using var f = new Font("Segoe UI", 9f * scale, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(e.Graphics, title, f, new Rectangle(x, 0, Width - x - 6, Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public void MoveTo(Point screen) => Location = new Point(screen.X - (int)(24 * scale), screen.Y - Height / 2);
}
