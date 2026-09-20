using System.Windows;
using DesktopPet.Engine;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>
/// Multi-monitor layout maths. These cover the case that originally confined the pet to the
/// primary display, plus the awkward arrangement that follows from fixing it: two screens whose
/// floors sit at different heights.
/// </summary>
public class DesktopGeometryTests
{
    /// <summary>
    /// A real two-monitor setup: a 3440x1440 primary with a taskbar, and a 1920x1080 secondary
    /// to its right that is physically lower and shorter, so their floors do not line up.
    /// </summary>
    private static DesktopGeometry TwoMonitors() => new(
        bounds: new Rect(0, 0, 5360, 1440),
        workAreas: new[]
        {
            new Rect(0, 0, 3440, 1392),       // primary, floor at y=1392
            new Rect(3440, 157, 1920, 1020),  // secondary, floor at y=1177
        });

    [Fact]
    public void Bounds_span_every_monitor_not_just_the_primary()
    {
        var d = TwoMonitors();
        Assert.Equal(0, d.Bounds.Left);
        Assert.Equal(5360, d.Bounds.Right);
    }

    [Theory]
    [InlineData(0, 1392)]       // far left of primary
    [InlineData(3439, 1392)]    // last pixel of primary
    [InlineData(3441, 1177)]    // first pixel of secondary
    [InlineData(5359, 1177)]    // far right of secondary
    public void FloorAt_returns_the_floor_of_the_monitor_under_that_x(double x, double expected)
    {
        Assert.Equal(expected, TwoMonitors().FloorAt(x));
    }

    [Fact]
    public void Crossing_between_monitors_changes_the_floor()
    {
        // This is the situation that made the pet fall forever: walking right off the primary
        // leaves its feet at 1392, which is BELOW the secondary's floor of 1177.
        var d = TwoMonitors();
        Assert.True(d.FloorAt(3441) < d.FloorAt(3439));
    }

    [Fact]
    public void WorkAreaAt_snaps_to_the_nearest_monitor_for_an_x_in_a_gap()
    {
        // Monitors need not tile: a gap can exist between two work areas.
        var d = new DesktopGeometry(
            new Rect(0, 0, 3000, 1000),
            new[] { new Rect(0, 0, 1000, 900), new Rect(2000, 0, 1000, 900) });

        Assert.Equal(0, d.WorkAreaAt(1200).Left);      // closer to the left monitor
        Assert.Equal(2000, d.WorkAreaAt(1900).Left);   // closer to the right one
    }

    [Fact]
    public void WorkAreaAt_clamps_for_x_outside_every_monitor()
    {
        var d = TwoMonitors();
        Assert.Equal(0, d.WorkAreaAt(-5000).Left);
        Assert.Equal(3440, d.WorkAreaAt(99999).Left);
    }

    [Fact]
    public void A_desktop_needs_at_least_one_work_area()
    {
        Assert.Throws<ArgumentException>(() =>
            new DesktopGeometry(new Rect(0, 0, 100, 100), Array.Empty<Rect>()));
    }

    [Fact]
    public void Capture_reports_the_real_machine_consistently()
    {
        // Not asserting specific numbers (they depend on the machine), but the invariants must
        // hold: at least one monitor, and every work area inside the overall bounds.
        var d = DesktopGeometry.Capture(1.0);

        Assert.NotEmpty(d.WorkAreas);
        foreach (var a in d.WorkAreas)
        {
            Assert.True(a.Left >= d.Bounds.Left,   $"{a} starts left of {d.Bounds}");
            Assert.True(a.Right <= d.Bounds.Right, $"{a} ends right of {d.Bounds}");
        }
    }
}
