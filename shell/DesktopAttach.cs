using static SLW3D.Shell.Native;

namespace SLW3D.Shell;

/// <summary>
/// Puts the shell window on the desktop layer: below every app window, above the desktop icons.
///
/// Windows 11 24H2 and later (this PC: 25H2 build 26200): the icons (SHELLDLL_DefView) and the
/// wallpaper (WorkerW) are both child windows of Progman. Older builds keep DefView inside a
/// top-level WorkerW. Either way we become a child of the window that hosts DefView and sit at
/// the top of its children, so we cover the icons (nothing is hidden or changed) and receive
/// mouse input directly. Because Progman is always the bottom-most top-level window, every app
/// still opens above us. If we exit or crash, our window disappears and the normal desktop is back.
/// </summary>
static class DesktopAttach
{
    public static (IntPtr host, IntPtr defView, string mode) Attach(IntPtr hwnd, bool layered)
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero, "Progman not found");

        // Ask Explorer to create the wallpaper WorkerW (standard live-wallpaper message; harmless if it exists).
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        string mode = "24H2+ (Progman hosts icons)";
        if (defView == IntPtr.Zero)
        {
            mode = "legacy (WorkerW hosts icons)";
            EnumWindows((h, _) =>
            {
                var dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero) { defView = dv; return false; }
                return true;
            }, IntPtr.Zero);
        }
        if (defView == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero, "desktop icon layer not found");

        IntPtr host = GetParent(defView);
        if (host == IntPtr.Zero) host = progman;

        long style = Style(hwnd, GWL_STYLE);
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        style |= WS_CHILD | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

        if (layered)
        {
            long ex = Style(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            mode += ", layered";
        }

        SetParent(hwnd, host);
        GetClientRect(host, out var r);
        SetWindowPos(hwnd, HWND_TOP, 0, 0, r.Width, r.Height, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        return (host, defView, mode);
    }

    /// <summary>Re-sizes to the host and places us above the desktop icons (without activating).</summary>
    public static void Refit(IntPtr hwnd, IntPtr host)
    {
        GetClientRect(host, out var r);
        if (r.Width <= 0 || r.Height <= 0) return;
        SetWindowPos(hwnd, HWND_TOP, 0, 0, r.Width, r.Height, SWP_NOACTIVATE);
    }

    /// <summary>True when our child window's client size no longer matches the desktop host (e.g. after a Windows
    /// DPI scale or resolution change where WinForms resizes the Form or Explorer resizes Progman asynchronously).</summary>
    public static bool NeedsSizeRefit(IntPtr hwnd, IntPtr host)
    {
        if (host == IntPtr.Zero || !IsWindow(host)) return false;
        GetClientRect(host, out var hr);
        GetClientRect(hwnd, out var cr);
        return hr.Width > 0 && hr.Height > 0 && (cr.Width != hr.Width || cr.Height != hr.Height);
    }

    /// <summary>True as long as <paramref name="defView"/> sits behind <paramref name="hwnd"/> in sibling z-order.
    /// Avoids unnecessary SetWindowPos churn if Explorer places a system helper child at the top of Progman.</summary>
    public static bool IsAboveIcons(IntPtr hwnd, IntPtr defView)
    {
        if (defView == IntPtr.Zero || !IsWindow(defView)) return true;
        // Walk from hwnd toward the bottom of sibling z-order (GW_HWNDNEXT = 2): if we reach defView, we are above it.
        for (IntPtr cur = GetWindow(hwnd, 2 /* GW_HWNDNEXT */); cur != IntPtr.Zero; cur = GetWindow(cur, 2))
            if (cur == defView) return true;
        return false;
    }
}
