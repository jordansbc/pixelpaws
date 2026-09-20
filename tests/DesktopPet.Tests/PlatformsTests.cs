using DesktopPet.Engine;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>Standing and landing. A pet that can't land reliably falls off the world.</summary>
public class PlatformsTests
{
    private static readonly Surface Floor  = new(0, 3440, 1392);
    private static readonly Surface Ledge  = new(500, 1200, 800);   // a window top
    private static readonly Surface[] World = { Floor, Ledge };

    [Fact]
    public void Standing_on_a_surface_counts_as_supported()
    {
        Assert.True(Platforms.IsSupported(World, centerX: 800, feetY: 800));
    }

    [Fact]
    public void Floating_above_a_surface_is_not_supported()
    {
        Assert.False(Platforms.IsSupported(World, centerX: 800, feetY: 700));
    }

    [Fact]
    public void Being_beside_a_ledge_rather_than_on_it_is_not_supported()
    {
        // Same height as the ledge, but past its right edge.
        Assert.False(Platforms.IsSupported(World, centerX: 1500, feetY: 800));
    }

    [Fact]
    public void Small_misalignment_is_tolerated()
    {
        Assert.True(Platforms.IsSupported(World, centerX: 800, feetY: 802));
        Assert.False(Platforms.IsSupported(World, centerX: 800, feetY: 810));
    }

    [Fact]
    public void Falling_lands_on_a_surface_crossed_this_frame()
    {
        bool landed = Platforms.TryLand(World, centerX: 800, prevFeet: 700, nextFeet: 850, out double top);
        Assert.True(landed);
        Assert.Equal(800, top);   // the ledge, not the distant floor
    }

    [Fact]
    public void Falling_picks_the_highest_surface_in_the_swept_span()
    {
        // A single fast frame can sweep past both the ledge and the floor; the ledge wins.
        bool landed = Platforms.TryLand(World, centerX: 800, prevFeet: 100, nextFeet: 2000, out double top);
        Assert.True(landed);
        Assert.Equal(800, top);
    }

    [Fact]
    public void Nothing_to_land_on_keeps_the_pet_falling()
    {
        Assert.False(Platforms.TryLand(World, centerX: 800, prevFeet: 100, nextFeet: 200, out _));
    }

    [Fact]
    public void A_surface_already_above_the_feet_cannot_be_landed_on()
    {
        // This is the multi-monitor trap: the pet's feet are BELOW the only surface under it,
        // so TryLand must refuse — which is why PetEngine.RescueBelowFloor has to exist.
        var secondaryFloorOnly = new[] { new Surface(3440, 5360, 1177) };
        Assert.False(Platforms.TryLand(secondaryFloorOnly, centerX: 4000,
                                       prevFeet: 1392, nextFeet: 1420, out _));
    }

    [Fact]
    public void An_empty_world_supports_nothing()
    {
        Assert.False(Platforms.IsSupported(Array.Empty<Surface>(), 0, 0));
        Assert.False(Platforms.TryLand(Array.Empty<Surface>(), 0, 0, 1000, out _));
    }
}
