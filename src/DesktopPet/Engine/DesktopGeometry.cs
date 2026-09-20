using System.Windows;
using DesktopPet.Native;

namespace DesktopPet.Engine;

/// <summary>
/// The desktop the pet actually lives on, in DIPs: the union of every attached monitor plus
/// each monitor's own work area (the part a taskbar doesn't cover).
///
/// WPF's <see cref="SystemParameters.WorkArea"/> describes the PRIMARY display only, so using it
/// for the pet's bounds confines the cat to one screen no matter how many are attached. Win32
/// reports monitor rects in physical pixels, so everything here is divided by the same
/// <c>dpiScale</c> the rest of the engine uses (see <see cref="SurfaceProvider"/>).
/// </summary>
public readonly struct DesktopGeometry
{
    /// <summary>Bounding box of every monitor together — the pet's hard travel limits.</summary>
    public Rect Bounds { get; }

    /// <summary>One work area per monitor. Each one's bottom edge is a floor the pet can stand on.</summary>
    public IReadOnlyList<Rect> WorkAreas { get; }

    /// <summary>Construct a layout directly. Public so a specific arrangement of monitors —
    /// including awkward ones that are hard to reproduce on real hardware — can be tested.</summary>
    public DesktopGeometry(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas is null || workAreas.Count == 0)
            throw new ArgumentException("A desktop needs at least one work area.", nameof(workAreas));

        Bounds    = bounds;
        WorkAreas = workAreas;
    }

    /// <summary>Read the current monitor layout. Cheap enough to re-read on the surface refresh tick,
    /// which is what makes the pet survive a monitor being plugged in or unplugged.</summary>
    public static DesktopGeometry Capture(double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale)) dpiScale = 1.0;

        var areas = new List<Rect>();
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;

        try
        {
            foreach (var (monitor, work) in Win32.GetMonitors())
            {
                if (work.Width <= 0 || work.Height <= 0) continue;
                areas.Add(new Rect(work.Left / dpiScale, work.Top / dpiScale,
                                   work.Width / dpiScale, work.Height / dpiScale));

                l = Math.Min(l, monitor.Left   / dpiScale);
                t = Math.Min(t, monitor.Top    / dpiScale);
                r = Math.Max(r, monitor.Right  / dpiScale);
                b = Math.Max(b, monitor.Bottom / dpiScale);
            }
        }
        catch { /* fall through to the primary-only fallback below */ }

        if (areas.Count == 0)
        {
            var work = SystemParameters.WorkArea;
            return new DesktopGeometry(work, new[] { work });
        }

        var geometry = new DesktopGeometry(new Rect(l, t, r - l, b - t), areas);
        LogIfChanged(geometry, dpiScale);
        return geometry;
    }

    // Capture runs several times a second; only say something when the layout actually moves.
    private static string _lastLogged = "";

    private static void LogIfChanged(DesktopGeometry g, double dpiScale)
    {
        if (!Services.DebugLog.Enabled) return;

        string summary = $"dpi={dpiScale:0.###} bounds={g.Bounds} areas=[" +
                         string.Join("; ", g.WorkAreas) + "]";
        if (summary == _lastLogged) return;
        _lastLogged = summary;
        Services.DebugLog.Write("Desktop geometry: " + summary);
    }

    /// <summary>The work area of the monitor at <paramref name="x"/>; the nearest monitor if that
    /// x falls in a gap between two displays.</summary>
    /// <remarks>
    /// Loops by index rather than with foreach: this is called every frame (via FloorAt from the
    /// ledge test and the fall rescue), and foreach over an IReadOnlyList&lt;T&gt; allocates a boxed
    /// enumerator each time.
    /// </remarks>
    public Rect WorkAreaAt(double x)
    {
        for (int i = 0; i < WorkAreas.Count; i++)
        {
            var a = WorkAreas[i];
            if (x >= a.Left && x <= a.Right) return a;
        }

        Rect best = WorkAreas[0];
        double bestDistance = double.MaxValue;
        for (int i = 0; i < WorkAreas.Count; i++)
        {
            var a = WorkAreas[i];
            double d = x < a.Left ? a.Left - x : x - a.Right;
            if (d < bestDistance) { bestDistance = d; best = a; }
        }
        return best;
    }

    /// <summary>The floor height (work-area bottom) of the monitor the given x sits on.</summary>
    public double FloorAt(double x) => WorkAreaAt(x).Bottom;
}
