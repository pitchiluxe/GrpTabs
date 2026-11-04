using GrpTabs.Core;

namespace GrpTabs.Ui;

/// <summary>
/// The running app: the engine, the notification-area icon and its menu,
/// the settings window, and updates. GrpTabs has no main window; closing
/// settings leaves it running, Exit stops it and shows every hidden window.
/// </summary>
internal sealed class Tray : ApplicationContext
{
    private static Tray? current;
    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem pauseItem, updateItem, ungroupItem;
    private readonly Updater updater = new();
    private Engine? engine;
    private SettingsForm? settingsForm;
    private Action? balloonAction;
    private readonly Control invoker = new();

    public Tray(bool startedWithWindows)
    {
        current = this;
        invoker.CreateControl();
        var st = Settings.Load();
        engine = new Engine(st);
        engine.Changed += UpdateMenu;
        updater.StateChanged += UpdateMenu;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        pauseItem = new ToolStripMenuItem("Pause", null, (_, _) => engine?.SetPaused(!(engine?.Paused ?? false)));
        ungroupItem = new ToolStripMenuItem("Ungroup all windows", null, (_, _) => engine?.UngroupAll());
        updateItem = new ToolStripMenuItem("Restart to update", null, (_, _) => updater.RestartToUpdate()) { Visible = false };
        menu.Items.Add(pauseItem);
        menu.Items.Add(ungroupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(updateItem);
        menu.Items.Add("Check for updates", null, async (_, _) => Notify("GrpTabs", await updater.CheckAsync(true)));
        menu.Items.Add("About GrpTabs", null, (_, _) => Notify("GrpTabs", $"Version {updater.CurrentVersion}. Created by Erick Omari."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThreadCore());

        icon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "GrpTabs",
            ContextMenuStrip = menu,
            Visible = st.TrayIcon,
        };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
        icon.BalloonTipClicked += (_, _) => { var a = balloonAction; balloonAction = null; a?.Invoke(); };

        engine.Start();
        UpdateMenu();
        updater.Start(st.CheckForUpdates);
        // Two tab tools fight over the same strip above every window.
        if (System.Diagnostics.Process.GetProcessesByName("TidyTabs.Daemon").Length > 0)
            Notify("GrpTabs and TidyTabs", "TidyTabs is also running. Both put tabs above windows, so the tabs overlap. Pause or exit one of them.");
        else if (engine.HotkeyFailures.Count > 0) Notify("GrpTabs hotkeys", $"Another app already uses: {string.Join(", ", engine.HotkeyFailures)}. Pick other keys in Settings > Hotkeys.");
        // Started from Start or the installer (not at sign-in): show settings so you can see it is running.
        if (!startedWithWindows) OpenSettings();
    }

    public static Settings CurrentSettings => current?.engine?.Settings ?? Settings.Load();
    public static Engine? CurrentEngine => current?.engine;
    public static Updater? CurrentUpdater => current?.updater;

    private void UpdateMenu()
    {
        if (engine is null) return;
        pauseItem.Text = engine.Paused ? "Resume" : "Pause";
        ungroupItem.Enabled = engine.Groups.Count > 0;
        updateItem.Visible = updater.ReadyVersion is not null;
        updateItem.Text = $"Restart to update to {updater.ReadyVersion}";
        icon.Text = $"GrpTabs {(engine.Paused ? "(paused) " : "")}- {engine.Groups.Count} group{(engine.Groups.Count == 1 ? "" : "s")}";
    }

    /// <summary>Applies new settings everywhere (engine, tray icon, update checks).</summary>
    public static void ApplySettings(Settings st)
    {
        if (current?.engine is null) return;
        st.Save();
        current.engine.Apply(st);
        current.icon.Visible = st.TrayIcon;
        current.updater.Start(st.CheckForUpdates);
    }

    public static void OpenSettings()
    {
        if (current is null) return;
        current.invoker.BeginInvoke(() =>
        {
            if (current.settingsForm is { IsDisposed: false } f) { f.WindowState = FormWindowState.Normal; f.Activate(); return; }
            current.settingsForm = new SettingsForm(current.updater);
            current.settingsForm.Show();
            current.settingsForm.Activate();
        });
    }

    /// <summary>A notification from the tray icon (a Windows toast). <paramref name="onClick"/> runs if it is clicked.</summary>
    public static void Notify(string title, string text, Action? onClick = null)
    {
        if (current is null || string.IsNullOrEmpty(text)) return;
        current.balloonAction = onClick;
        bool wasVisible = current.icon.Visible;
        current.icon.Visible = true;
        current.icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
        if (!wasVisible) Task.Delay(8000).ContinueWith(_ => current.invoker.BeginInvoke(() => current.icon.Visible = CurrentSettings.TrayIcon));
    }

    public static void ShutdownEngine()
    {
        if (current?.engine is null) return;
        current.engine.Dispose();
        current.engine = null;
    }

    public static void ExitApp() => current?.ExitThreadCore();

    protected override void ExitThreadCore()
    {
        ShutdownEngine();
        updater.ApplyOnExit();
        icon.Visible = false;
        icon.Dispose();
        settingsForm?.Close();
        base.ExitThreadCore();
    }
}

/// <summary>The GrpTabs icon, embedded in the program.</summary>
internal static class AppIcon
{
    public static Icon Load()
    {
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("GrpTabs.ico");
        return s is null ? SystemIcons.Application : new Icon(s);
    }

    public static Image? Logo()
    {
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("logo.png");
        return s is null ? null : Image.FromStream(s);
    }
}
