using System.Windows;
using DesktopPet.Native;

namespace DesktopPet.Engine;

/// <summary>A walkable horizontal platform, in DIP coordinates.</summary>
public readonly struct Surface
{
    public readonly double Left;
    public readonly double Right;
    public readonly double Top;

    public Surface(double left, double right, double top)
    {
        Left = left;
        Right = right;
        Top = top;
    }

    public bool ContainsX(double x) => x >= Left && x <= Right;
}

/// <summary>
/// Produces the set of platforms the pet can stand on: the desktop work-area floor plus the
/// top edge of every eligible top-level window. Win32 rects (physical px) are converted to DIPs.
/// </summary>
public sealed class SurfaceProvider
{
    private readonly Func<IntPtr> _selfHandle;

    public SurfaceProvider(Func<IntPtr> selfHandle)
    {
        _selfHandle = selfHandle;
    }

    public List<Surface> GetSurfaces(double dpiScale, DesktopGeometry desktop)
    {
        // One floor per monitor: each sits just above that screen's taskbar. A single
        // primary-only floor would strand the pet the moment it crossed onto another display.
        var surfaces = new List<Surface>(desktop.WorkAreas.Count + 8);
        for (int i = 0; i < desktop.WorkAreas.Count; i++)
        {
            var area = desktop.WorkAreas[i];
            surfaces.Add(new Surface(area.Left, area.Right, area.Bottom));
        }

        var bounds = desktop.Bounds;
        IntPtr self = _selfHandle();
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!IsEligible(hWnd, self)) return true;
            if (!Win32.GetWindowRect(hWnd, out var r)) return true;

            // Convert physical px -> DIPs.
            double left = r.Left / dpiScale;
            double right = r.Right / dpiScale;
            double top = r.Top / dpiScale;

            // Ignore off-screen / absurd rects. Vertical limits come from the monitor the
            // window's top edge actually sits on, not from the primary display.
            var area = desktop.WorkAreaAt((left + right) / 2);
            if (right - left < 80 || top < area.Top - 4 || top > area.Bottom) return true;

            // Clamp horizontally to the whole desktop so the pet never walks off every screen.
            left = Math.Max(left, bounds.Left);
            right = Math.Min(right, bounds.Right);
            if (right - left < 40) return true;

            surfaces.Add(new Surface(left, right, top));
            return true;
        }, IntPtr.Zero);

        return surfaces;
    }

    private static bool IsEligible(IntPtr hWnd, IntPtr self)
    {
        if (hWnd == self) return false;
        if (!Win32.IsWindowVisible(hWnd)) return false;
        if (Win32.IsIconic(hWnd)) return false;
        if (Win32.IsCloaked(hWnd)) return false;
        if (Win32.GetWindowTextLength(hWnd) == 0) return false;

        int ex = Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return false; // tool windows aren't "real" windows

        return true;
    }
}
