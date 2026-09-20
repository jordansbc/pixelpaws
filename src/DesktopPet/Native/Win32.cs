using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.Native;

/// <summary>The states SHQueryUserNotificationState can report. Public (rather than nested in
/// the internal Win32 class) so the mapping that consumes it can be unit-tested.</summary>
public enum UserNotificationState
{
    NotPresent            = 1, // screensaver / locked / logged off
    Busy                  = 2, // a full-screen app is running (not D3D)
    RunningD3dFullScreen  = 3, // a full-screen D3D app - usually a game
    PresentationMode      = 4, // presentation mode is on
    AcceptsNotifications  = 5, // the normal, interruptible case
    QuietTime             = 6, // the first hour after a fresh install, or focus assist
    AppScreenSaver        = 7,
}

/// <summary>
/// Thin P/Invoke layer. All coordinates returned here are PHYSICAL pixels
/// (screen space), which the engine converts to DIPs via the active DPI scale.
/// </summary>
internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // Extended window styles / attributes we care about.
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int DWMWA_CLOAKED = 14;

    /// <summary>Make a window click-through (mouse events pass to whatever is underneath).</summary>
    public static void MakeClickThrough(IntPtr hWnd)
    {
        int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>True if DWM reports the window as cloaked (e.g. virtual-desktop hidden, UWP suspended).</summary>
    public static bool IsCloaked(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0)
            return false;
        return cloaked != 0;
    }

    // ── Monitors ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;   // full monitor rect, virtual-screen physical pixels
        public RECT rcWork;      // minus taskbar/appbars, same coordinate space
        public int dwFlags;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    /// <summary>
    /// Every monitor's full and working rectangle, in physical virtual-screen pixels.
    ///
    /// This deliberately uses Win32 rather than WinForms' Screen class: under PerMonitorV2
    /// awareness, Screen's coordinates depend on the caller's DPI context and can come back
    /// already scaled, which silently disagrees with the physical pixels GetWindowRect hands
    /// the surface scanner. GetMonitorInfo is always physical, so every rect the engine sees
    /// lives in one coordinate space.
    /// </summary>
    public static List<(RECT Monitor, RECT Work)> GetMonitors()
    {
        var found = new List<(RECT, RECT)>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(h, ref info))
                found.Add((info.rcMonitor, info.rcWork));
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ── "Is the user presenting?" ───────────────────────────────────────────

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);

    /// <summary>
    /// Ask Windows whether now is a good moment to be noticed. This is the same signal the OS
    /// uses to decide whether to show toast notifications, so it covers screen sharing (which
    /// puts most conferencing apps full-screen), games, presentation mode and focus assist.
    /// Returns <see cref="UserNotificationState.AcceptsNotifications"/> if the call fails, so a
    /// failure leaves the pet behaving normally rather than silently muting it forever.
    /// </summary>
    public static UserNotificationState GetUserNotificationState()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0) return state;
        }
        catch { /* shell32 unavailable — assume it is fine to play */ }
        return UserNotificationState.AcceptsNotifications;
    }

    // ── Global keyboard hook ────────────────────────────────────────────────

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    // Low-level mouse hook (same callback signature as the keyboard hook).
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEWHEEL = 0x020A;

    /// <summary>
    /// Wheel delta for the most recent WM_MOUSEWHEEL. The signed 16-bit delta lives in the
    /// high word of the MSLLHOOKSTRUCT.mouseData field, which sits at byte offset 8
    /// (POINT pt = 8 bytes, then mouseData). One notch == 120.
    /// </summary>
    public static int ReadWheelDelta(IntPtr lParam)
    {
        int mouseData = Marshal.ReadInt32(lParam, 8);
        return (short)((mouseData >> 16) & 0xFFFF);
    }
}
