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

    public List<Surface> GetSurfaces(double dpiScale)
    {
        var work = SystemParameters.WorkArea; // already DIPs
        var surfaces = new List<Surface>
        {
            // The floor: sits just above the taskbar, spanning the whole work area.
            new Surface(work.Left, work.Right, work.Bottom)
        };

        IntPtr self = _selfHandle();
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!IsEligible(hWnd, self)) return true;
            if (!Win32.GetWindowRect(hWnd, out var r)) return true;

            // Convert physical px -> DIPs.
            double left = r.Left / dpiScale;
            double right = r.Right / dpiScale;
            double top = r.Top / dpiScale;

            // Ignore off-screen / absurd rects.
            if (right - left < 80 || top < work.Top - 4 || top > work.Bottom) return true;

            // Clamp horizontally to the work area so the pet never walks off the visible desktop.
            left = Math.Max(left, work.Left);
            right = Math.Min(right, work.Right);
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
