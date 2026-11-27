using System.Diagnostics;
using static SLW3D.Shell.Native;

namespace SLW3D.Shell;

/// <summary>Is the primary monitor's desktop fully covered by a maximized or full-screen app?
/// If so the shell stops rendering (near-zero GPU) until it is visible again.</summary>
static class Occlusion
{
    static readonly HashSet<string> Ignore = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow",
    };

    // Transparent/transient desktop overlays (e.g. PowerToys utilities) should never falsely hide the desktop.
    static readonly HashSet<string> IgnoreProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PowerToys.AlwaysOnTop", "PowerToys.ColorPickerUI", "PowerToys.FancyZones",
        "PowerToys.FindMyMouse", "PowerToys.MeasureToolUI", "PowerToys.MouseHighlighter",
        "PowerToys.ShortcutGuide",
    };

    public static bool PrimaryDesktopCovered()
    {
        IntPtr primary = MonitorFromPoint(new System.Drawing.Point(0, 0), 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(primary, ref mi)) return false;
        var m = mi.rcMonitor;

        bool covered = false;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
            long ex = Style(h, GWL_EXSTYLE);
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return true; // overlays, tool palettes
            if ((ex & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(h, out uint _, out byte alpha, out uint lwa) && (lwa & LWA_ALPHA) != 0 && alpha == 0)
                return true; // invisible layered overlay window
            if (Ignore.Contains(ClassName(h))) return true;
            if (MonitorFromWindow(h, 0 /* NULL */) != primary) return true;
            bool full = IsZoomed(h) || (GetWindowRect(h, out var r) && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom);
            if (!full) return true;
            if (IsIgnoredOverlayProcess(h)) return true;
            covered = true;
            return false;
        }, IntPtr.Zero);
        return covered;
    }

    static bool IsIgnoredOverlayProcess(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == 0) return false;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return IgnoreProcesses.Contains(p.ProcessName);
        }
        catch { return false; }
    }
}
