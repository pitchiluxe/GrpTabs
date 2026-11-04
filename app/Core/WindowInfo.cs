using System.Runtime.InteropServices;
using System.Text;

namespace GrpTabs.Core;

/// <summary>Which top-level windows get tabs, and the facts about them.</summary>
internal static class WindowInfo
{
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static readonly Dictionary<uint, string> ExeCache = new();

    /// <summary>Shell and system surfaces that are never app windows.</summary>
    private static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "ForegroundStaging", "MultitaskingViewFrame",
    };

    public static uint Pid(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>The program file name ("notepad.exe"), cached per process.</summary>
    public static string Exe(IntPtr hwnd)
    {
        uint pid = Pid(hwnd);
        if (ExeCache.TryGetValue(pid, out var name)) return name;
        name = "";
        var h = OpenProcess(0x1000, false, pid);
        if (h != IntPtr.Zero)
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(h, 0, sb, ref size)) name = Path.GetFileName(sb.ToString());
            CloseHandle(h);
        }
        if (ExeCache.Count > 512) ExeCache.Clear();
        ExeCache[pid] = name;
        return name;
    }

    public static float Scale(IntPtr hwnd)
    {
        try { uint dpi = GetDpiForWindow(hwnd); return dpi > 0 ? dpi / 96f : 1f; }
        catch (EntryPointNotFoundException) { return 1f; }
    }

    /// <summary>A normal, visible app window with a title bar: the kind that gets tabs.</summary>
    public static bool Eligible(IntPtr hwnd, bool ignoreVisibility = false)
    {
        if (!Native.IsWindow(hwnd)) return false;
        if (!ignoreVisibility && (!Native.IsWindowVisible(hwnd) || Native.IsCloaked(hwnd))) return false;
        if (Pid(hwnd) == OwnPid) return false;
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        if ((style & Native.WS_CHILD) != 0 || (style & Native.WS_CAPTION) != Native.WS_CAPTION) return false;
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
        if ((ex & Native.WS_EX_NOACTIVATE) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (SkipClasses.Contains(Native.ClassName(hwnd))) return false;
        if (Native.Title(hwnd).Length == 0) return false;
        var r = Native.Frame(hwnd);
        return Native.IsIconic(hwnd) || (r.Width >= 220 && r.Height >= 120);
    }

    public static WinFacts Facts(IntPtr hwnd) => new(hwnd, Exe(hwnd), Native.IsZoomed(hwnd), Native.IsIconic(hwnd), FullScreen(hwnd));

    /// <summary>Covers its whole monitor, taskbar included (games, videos, presentations), and is not just maximized.</summary>
    public static bool FullScreen(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) return false;
        var f = Native.Frame(hwnd);
        var m = Screen.FromHandle(hwnd).Bounds;
        return f.Left <= m.Left && f.Top <= m.Top && f.Right >= m.Right && f.Bottom >= m.Bottom;
    }

    public static List<IntPtr> TopLevel()
    {
        var list = new List<IntPtr>();
        Native.EnumWindows((h, _) => { if (Eligible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
}
