namespace GrpTabs.Core;

/// <summary>Opt-in diagnostics: set GRPTABS_DEBUG=1 to write %AppData%\GrpTabs\debug.log.</summary>
internal static class Log
{
    public static readonly bool On = Environment.GetEnvironmentVariable("GRPTABS_DEBUG") == "1";

    public static void Write(string msg)
    {
        if (!On) return;
        try { Directory.CreateDirectory(Settings.Folder); File.AppendAllText(Path.Combine(Settings.Folder, "debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
        catch (IOException) { }
    }
}
