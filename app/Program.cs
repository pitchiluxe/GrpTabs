using GrpTabs.Core;
using GrpTabs.Ui;
using Velopack;

namespace GrpTabs;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Installer hooks run first and exit: start with Windows on install, clean up on uninstall.
        VelopackApp.Build()
            .OnFirstRun(_ => { try { Settings.StartsWithWindows = true; } catch (Exception) { } })
            .OnBeforeUninstallFastCallback(_ => { try { Settings.StartsWithWindows = false; } catch (Exception) { } })
            .Run();

        // One GrpTabs at a time: starting it again opens the running one's settings.
        using var mutex = new Mutex(true, @"Local\GrpTabs.Erick.Omari", out bool first);
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GrpTabs.Erick.Omari.Show");
        if (!first)
        {
            signal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);

        var tray = new Tray(args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
        var listener = new Thread(() => { while (signal.WaitOne()) Tray.OpenSettings(); }) { IsBackground = true, Name = "GrpTabs show" };
        listener.Start();
        // Signing out or shutting down: bring parked tabs back before apps close and save their positions.
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => { try { Tray.ShutdownEngine(); } catch (Exception) { } };
        Application.Run(tray);
    }

    /// <summary>Never leave windows hidden: show them all, write the error down, then stop.</summary>
    private static void Crash(Exception? ex)
    {
        try { Tray.ShutdownEngine(); } catch (Exception) { }
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), $"{DateTime.Now:u} {ex}\n\n");
        }
        catch (Exception) { }
        Environment.Exit(1);
    }
}
