using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// Smooth tab switching: a snapshot of the outgoing window is laid over the
/// frame, the windows are swapped underneath instantly, and the snapshot fades
/// out. Other apps' windows are never changed, so it works with every app.
/// Skipped when Windows' "Animation effects" setting is off.
/// </summary>
internal static class Crossfade
{
    private const int DurationMs = 180;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    public static bool WindowsAnimates()
    {
        try { return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out bool on, 0) || on; }
        catch (EntryPointNotFoundException) { return true; }
    }

    /// <summary>Runs <paramref name="swap"/> behind a fading snapshot of <paramref name="frame"/> (screen pixels).</summary>
    public static void Run(Rectangle frame, bool enabled, Action swap)
    {
        if (!enabled || !WindowsAnimates() || frame.Width < 40 || frame.Height < 40)
        {
            swap();
            return;
        }
        Bitmap shot;
        try
        {
            shot = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(shot);
            g.CopyFromScreen(frame.Location, Point.Empty, frame.Size, CopyPixelOperation.SourceCopy);
        }
        catch (Exception)
        {
            swap();
            return;
        }
        var veil = new Veil(shot, frame);
        veil.Show();
        swap();
        veil.FadeOut(DurationMs);
    }

    /// <summary>The snapshot window: on top, never focused, clicks pass through.</summary>
    private sealed class Veil : Form
    {
        private readonly Bitmap shot;
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };

        public Veil(Bitmap shot, Rectangle frame)
        {
            this.shot = shot;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = frame;
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
            Layered.Present(Handle, shot, Location, 255);
        }

        /// <summary>Fade from opaque to clear with an ease-out curve, then go away.</summary>
        public void FadeOut(int ms)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, clock.ElapsedMilliseconds / (double)ms);
                double eased = 1 - Math.Pow(1 - t, 3);
                if (t >= 1) { timer.Stop(); Close(); return; }
                Layered.Present(Handle, shot, Location, (byte)Math.Round(255 * (1 - eased)));
            };
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); shot.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
