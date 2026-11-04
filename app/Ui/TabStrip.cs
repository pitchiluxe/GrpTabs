using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// The tabs above one window: a borderless layered window owned by that
/// window (Windows keeps it just above it and hides it with it) that never
/// takes the focus. It is drawn once into a bitmap and handed to Windows with
/// UpdateLayeredWindow, so moving it while you drag a window is only a move,
/// and the see-through amount changes without a repaint: nothing flickers.
/// It is redrawn only when what it shows changes.
/// </summary>
internal sealed class TabStrip : Form
{
    private readonly Engine engine;
    private readonly List<(IntPtr Hwnd, Rectangle Box, Rectangle Close)> layout = new();
    private readonly ToolTip tip = new() { InitialDelay = 600, ReshowDelay = 200, ShowAlways = true };
    private Bitmap? image;
    private string drawnKey = "";
    private Rectangle placed;
    private byte alpha = 255;
    private Point downAt;
    private IntPtr downTab;
    private bool dragging;
    private bool hot;
    private IntPtr hoverTab;
    private bool hoverClose;
    private IntPtr tipFor;

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
        AutoScaleMode = AutoScaleMode.None;
        Text = "GrpTabs";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) | Layered.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, Target);
        drawnKey = "";
    }

    // Layered windows never paint through WM_PAINT; the bitmap is the window.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    /// <summary>Follow another window (the group's active tab changed).</summary>
    public void Retarget(IntPtr target)
    {
        if (Target == target) return;
        Target = target;
        tip.Hide(this);
        tipFor = IntPtr.Zero;
        if (IsHandleCreated) Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, target);
    }

    public void SetTabs(IReadOnlyList<IntPtr> tabs) => Tabs = tabs.ToList();

    /// <summary>Above the window's top edge, or over its title bar when there is no room (maximized windows).</summary>
    public void Reposition()
    {
        if (!IsHandleCreated) CreateHandle();
        var st = engine.Settings;
        UiScale = WindowInfo.Scale(Target);
        var f = Native.Frame(Target);
        int h = (int)Math.Round(28 * UiScale);
        int maxWidth = Math.Max((int)(120 * UiScale), f.Width - (int)(150 * UiScale));
        LayoutTabs(st, h, maxWidth);
        int width = layout.Count > 0 ? Math.Min(layout[^1].Box.Right + 2, maxWidth) : 10;
        var work = Screen.FromHandle(Target).WorkingArea;
        int top = f.Top - h;
        int left = f.Left + (int)(12 * UiScale);
        if (top < work.Top) { top = f.Top; left = f.Left + (int)(46 * UiScale); }
        var want = new Rectangle(left, top, width, h);
        byte wantAlpha = (byte)Math.Round(255 * (hot || DropHighlight ? 1.0 : st.IdleOpacity / 100.0));
        string key = Key(st, want.Size);
        if (key != drawnKey || image is null || want.Size != placed.Size)
        {
            Render(st, want.Size);
            drawnKey = key;
            placed = want;
            alpha = wantAlpha;
            Layered.Present(Handle, image!, want.Location, alpha);
            Native.SetWindowPos(Handle, IntPtr.Zero, want.X, want.Y, want.Width, want.Height, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | Native.SWP_NOOWNERZORDER);
        }
        else if (want.Location != placed.Location)
        {
            placed = want;
            Native.SetWindowPos(Handle, IntPtr.Zero, want.X, want.Y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | Native.SWP_NOSIZE | Native.SWP_NOOWNERZORDER);
        }
        if (wantAlpha != alpha)
        {
            alpha = wantAlpha;
            Layered.Present(Handle, image!, placed.Location, alpha);
        }
        KeepAbove();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) KeepAbove();
    }

    /// <summary>
    /// Sit directly above our window in the stacking order. Ownership alone does
    /// not keep this for another program's window, so the strip places itself:
    /// just under whatever is directly above the window, so it goes behind
    /// other windows exactly when its own window does.
    /// </summary>
    public void KeepAbove()
    {
        if (!IsHandleCreated || !Visible || !Native.IsWindow(Target)) return;
        var above = Native.GetWindow(Target, Native.GW_HWNDPREV);
        if (above == Handle) return;
        // Never slip into the always-on-top band unless our window is in it.
        bool targetTop = (Native.GetWindowLongPtr(Target, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST) != 0;
        if (above != IntPtr.Zero && !targetTop && (Native.GetWindowLongPtr(above, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST) != 0) above = IntPtr.Zero;
        Native.SetWindowPos(Handle, above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    /// <summary>Everything the picture depends on; the strip is redrawn only when this changes.</summary>
    private string Key(Settings st, Size size)
    {
        var active = engine.ActiveOf(Target);
        return string.Join('|', size.Width, size.Height, UiScale, st.TabColor, st.Theme, Palette.IsDark(st.Theme), st.ShowIcons, st.CloseButtons, st.TabWidth,
            DropHighlight, hoverTab, hoverClose, active, string.Join(',', Tabs.Select(t => $"{t}:{Native.Title(t)}")));
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

    private static readonly Bitmap Measure = new(1, 1);

    private void LayoutTabs(Settings st, int h, int maxWidth)
    {
        layout.Clear();
        using var g = Graphics.FromImage(Measure);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        int x = 0;
        int maxW = (int)(st.TabWidth * UiScale), minW = (int)(78 * UiScale);
        int closeSz = (int)(16 * UiScale);
        foreach (var hwnd in Tabs)
        {
            int text = (int)Math.Ceiling(g.MeasureString(Native.Title(hwnd), TabFont).Width);
            int extra = (int)((st.ShowIcons ? 40 : 22) * UiScale) + (st.CloseButtons ? closeSz + (int)(4 * UiScale) : 0);
            int w = Math.Clamp(text + extra, minW, maxW);
            if (x + w > maxWidth && layout.Count > 0) w = Math.Max(minW, maxWidth - x);
            var box = new Rectangle(x, 0, w, h);
            var close = st.CloseButtons ? new Rectangle(box.Right - closeSz - (int)(7 * UiScale), (h - closeSz) / 2 + (int)(1 * UiScale), closeSz, closeSz) : Rectangle.Empty;
            layout.Add((hwnd, box, close));
            x += w + (int)(2 * UiScale);
        }
    }

    private void Render(Settings st, Size size)
    {
        image?.Dispose();
        image = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        var pal = Palette.For(st);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var active = engine.ActiveOf(Target);
        int r = (int)(8 * UiScale);
        using var fmt = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        foreach (var (hwnd, box, close) in layout)
        {
            if (box.Left >= size.Width) break;
            bool on = hwnd == active;
            int inset = on ? 0 : (int)(2 * UiScale);
            var rect = new Rectangle(box.X, box.Y + inset, Math.Min(box.Width, size.Width - box.X - 1), box.Height - inset);
            using (var path = Rounded(rect, r))
            {
                using (var fill = new SolidBrush(on ? pal.On : hwnd == hoverTab ? pal.Hover : pal.Tab)) g.FillPath(fill, path);
                using (var pen = new Pen(on ? pal.On : pal.Line, Math.Max(1f, UiScale))) g.DrawPath(pen, path);
            }
            int pad = (int)(10 * UiScale);
            int textLeft = rect.X + pad;
            if (st.ShowIcons)
            {
                int sz = (int)(16 * UiScale);
                if (engine.IconOf(hwnd) is { } icon) g.DrawImage(icon, rect.X + pad, rect.Y + (rect.Height - sz) / 2, sz, sz);
                textLeft += sz + (int)(6 * UiScale);
            }
            bool showClose = st.CloseButtons && (on || hwnd == hoverTab);
            int textRight = (st.CloseButtons ? close.Left - (int)(2 * UiScale) : rect.Right - pad / 2);
            using (var ink = new SolidBrush(on ? pal.OnInk : pal.TabInk))
                g.DrawString(Native.Title(hwnd), TabFont, ink, new RectangleF(textLeft, rect.Y, Math.Max(1, textRight - textLeft), rect.Height), fmt);
            if (showClose)
            {
                var c = close;
                bool overClose = hoverClose && hwnd == hoverTab;
                if (overClose) using (var bg = new SolidBrush(pal.Close)) g.FillEllipse(bg, c);
                using var pen = new Pen(overClose ? Color.White : on ? pal.OnInk : pal.TabInk, Math.Max(1.2f, 1.4f * UiScale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float m = c.Width * 0.3f;
                g.DrawLine(pen, c.Left + m, c.Top + m, c.Right - m, c.Bottom - m);
                g.DrawLine(pen, c.Right - m, c.Top + m, c.Left + m, c.Bottom - m);
            }
        }
        if (DropHighlight)
        {
            using var pen = new Pen(pal.On, 2 * UiScale);
            g.DrawRectangle(pen, 1, 1, size.Width - 3, size.Height - 3);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    public IntPtr TabAt(Point client) => layout.FirstOrDefault(l => l.Box.Contains(client)).Hwnd;

    private bool OverClose(Point client) => layout.Any(l => l.Close.Contains(client) && engine.Settings.CloseButtons);

    /// <summary>Where a tab dropped at this screen point goes in the tab order.</summary>
    public int IndexAt(Point screen)
    {
        var p = new Point(screen.X - placed.X, screen.Y - placed.Y);
        for (int i = 0; i < layout.Count; i++)
            if (p.X < layout[i].Box.Left + layout[i].Box.Width / 2) return i;
        return layout.Count;
    }

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
        bool c = t != IntPtr.Zero && OverClose(e.Location);
        if (t != hoverTab || c != hoverClose)
        {
            hoverTab = t;
            hoverClose = c;
            Reposition();
        }
        if (t != tipFor)
        {
            tipFor = t;
            tip.SetToolTip(this, t == IntPtr.Zero ? null : c ? "Close" : Native.Title(t));
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; engine.StripHover(this, true); Reposition(); }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = false;
        hoverTab = IntPtr.Zero;
        hoverClose = false;
        tipFor = IntPtr.Zero;
        tip.Hide(this);
        tip.SetToolTip(this, null);
        engine.StripHover(this, false);
        if (!IsDisposed && IsHandleCreated) Reposition();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (OverClose(e.Location)) { downTab = IntPtr.Zero; return; }
        downTab = TabAt(e.Location);
        downAt = e.Location;
        dragging = false;
        Capture = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var tab = TabAt(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            var was = downTab;
            downTab = IntPtr.Zero;
            if (dragging) { dragging = false; engine.Drop(was, Cursor.Position); }
            else if (tab != IntPtr.Zero && OverClose(e.Location)) engine.CloseTab(tab);
            else if (was != IntPtr.Zero) engine.ActivateTab(was);
        }
        else if (e.Button == MouseButtons.Middle && tab != IntPtr.Zero && engine.Settings.MiddleClickCloses) engine.CloseTab(tab);
        else if (e.Button == MouseButtons.Right && tab != IntPtr.Zero) engine.ShowTabMenu(this, tab, Cursor.Position);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (engine.Settings.WheelSwitches && Tabs.Count > 1) engine.CycleGroup(Target, e.Delta > 0 ? -1 : 1);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var tab = TabAt(e.Location);
        if (e.Button == MouseButtons.Left && tab != IntPtr.Zero && !OverClose(e.Location) && engine.Settings.DoubleClickMaximizes)
            Native.ShowWindow(tab, Native.IsZoomed(tab) ? Native.SW_RESTORE : Native.SW_SHOWMAXIMIZED);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { font?.Dispose(); image?.Dispose(); tip.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>UpdateLayeredWindow: show a premultiplied-alpha bitmap as a window, at a position and overall opacity.</summary>
internal static class Layered
{
    public const int WS_EX_LAYERED = 0x80000;
    private const int ULW_ALPHA = 2;
    private const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Native.POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref Native.POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    public static void Present(IntPtr hwnd, Bitmap bmp, Point at, byte alpha)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hbmp);
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new Native.POINT();
            var dst = new Native.POINT { X = at.X, Y = at.Y };
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

/// <summary>The tab that follows the pointer while you drag it: see-through to clicks, always on top.</summary>
internal sealed class DragGhost : Form
{
    private readonly string title;
    private readonly Image? icon;
    private readonly float scale;
    private readonly Color color;

    public DragGhost(string title, Image? icon, float scale, Color color)
    {
        this.title = title;
        this.icon = icon;
        this.scale = scale;
        this.color = color;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size((int)(200 * scale), (int)(28 * scale));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) | 0x20 /* WS_EX_TRANSPARENT */ | Layered.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = new GraphicsPath())
            {
                int d = (int)(16 * scale);
                path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width - d - 1, 0, d, d, 270, 90);
                path.AddArc(Width - d - 1, Height - d - 1, d, d, 0, 90); path.AddArc(0, Height - d - 1, d, d, 90, 90);
                path.CloseFigure();
                using var fill = new SolidBrush(color);
                g.FillPath(fill, path);
            }
            int x = (int)(10 * scale), sz = (int)(16 * scale);
            if (icon is not null) { g.DrawImage(icon, x, (Height - sz) / 2, sz, sz); x += sz + (int)(6 * scale); }
            using var f = new Font("Segoe UI", 9f * scale, FontStyle.Bold, GraphicsUnit.Point);
            using var fmt = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var ink = new SolidBrush(TabColors.InkOn(color));
            g.DrawString(title, f, ink, new RectangleF(x, 0, Width - x - 6, Height), fmt);
        }
        Layered.Present(Handle, bmp, Location, 225);
    }

    public void MoveTo(Point screen)
    {
        var p = new Point(screen.X - (int)(24 * scale), screen.Y - Height / 2);
        Native.SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | Native.SWP_NOSIZE);
    }
}
