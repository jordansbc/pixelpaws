using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>
/// Release-tag comparison. Getting this wrong either nags the user about an update forever or
/// silently never offers one, so the edge cases matter more than the happy path.
/// </summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3-beta", 1, 2, 3)]     // suffixes are cut, not rejected
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    public void ParseTag_understands_the_shapes_a_release_tag_takes(
        string tag, int major, int minor, int build)
    {
        var v = AppVersion.ParseTag(tag);
        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("nightly-build")]
    public void ParseTag_rejects_anything_that_is_not_a_version(string? tag)
    {
        Assert.Null(AppVersion.ParseTag(tag));
    }

    [Fact]
    public void A_much_older_tag_is_never_newer_than_the_running_build()
    {
        Assert.False(AppVersion.IsNewerThanCurrent("v0.0.1"));
    }

    [Fact]
    public void A_far_future_tag_is_newer()
    {
        Assert.True(AppVersion.IsNewerThanCurrent("v999.0.0"));
    }

    [Fact]
    public void The_running_version_is_not_newer_than_itself()
    {
        // The exact self-comparison: this is what stops a permanent "update available" badge.
        Assert.False(AppVersion.IsNewerThanCurrent(AppVersion.Display));
    }

    [Fact]
    public void Garbage_never_counts_as_an_update()
    {
        Assert.False(AppVersion.IsNewerThanCurrent("not-a-version"));
        Assert.False(AppVersion.IsNewerThanCurrent(null));
    }
}
