using Velopack;
using Velopack.Sources;

namespace GrpTabs.Ui;

/// <summary>
/// Automatic updates from GitHub Releases (Velopack). Checks at start and every
/// six hours while "Automatically check for updates" is on, downloads in the
/// background, then tells you an update is ready; it installs when you choose
/// Restart, or the next time GrpTabs closes.
/// </summary>
internal sealed class Updater
{
    public const string RepoUrl = "https://github.com/pitchiluxe/GrpTabs";
    private readonly UpdateManager? mgr;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 6 * 60 * 60 * 1000 };
    private UpdateInfo? ready;
    private bool busy;

    public event Action? StateChanged;
    public string? ReadyVersion => ready?.TargetFullRelease.Version.ToString();
    public bool Installed => mgr?.IsInstalled == true;

    public Updater()
    {
        try { mgr = new UpdateManager(new GithubSource(RepoUrl, null, false)); }
        catch (Exception) { mgr = null; }
        timer.Tick += async (_, _) => await CheckAsync(false);
    }

    public string CurrentVersion => mgr?.CurrentVersion?.ToString() ?? Application.ProductVersion.Split('+')[0];

    public void Start(bool auto)
    {
        timer.Enabled = auto && Installed;
        if (auto && Installed) _ = CheckAsync(false);
    }

    /// <summary>Checks GitHub; returns a sentence for the settings window. Interactive checks report "up to date" too.</summary>
    public async Task<string> CheckAsync(bool interactive)
    {
        if (mgr is null || !mgr.IsInstalled) return "Updates work in the installed app (this copy runs from a build folder).";
        if (busy) return "Already checking for updates.";
        if (ready is not null) return $"GrpTabs {ReadyVersion} is ready: restart to install it.";
        busy = true;
        try
        {
            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return $"GrpTabs {CurrentVersion} is up to date.";
            await mgr.DownloadUpdatesAsync(info);
            ready = info;
            StateChanged?.Invoke();
            Tray.Notify("GrpTabs update ready", $"Version {ReadyVersion} is downloaded. Click here or choose Restart to update in the tray menu.", () => RestartToUpdate());
            return $"GrpTabs {ReadyVersion} is ready: restart to install it.";
        }
        catch (Exception ex)
        {
            if (interactive) return $"Could not check for updates: {ex.Message}";
            return "";
        }
        finally { busy = false; }
    }

    /// <summary>Close GrpTabs cleanly (every hidden window shown again), install and start the new version.</summary>
    public void RestartToUpdate()
    {
        if (mgr is null || ready is null) return;
        Tray.ShutdownEngine();
        mgr.ApplyUpdatesAndRestart(ready.TargetFullRelease);
    }

    /// <summary>Install a downloaded update when GrpTabs exits, without restarting it.</summary>
    public void ApplyOnExit()
    {
        if (mgr is null || ready is null) return;
        try { mgr.WaitExitThenApplyUpdates(ready.TargetFullRelease, silent: true, restart: false); } catch (Exception) { }
    }
}
