using DesktopPet.Native;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>
/// Deciding when the cat should keep its head down.
///
/// The Win32 signal itself can only be exercised on a machine that is actually presenting or
/// gaming, so these cover the decision made from it — including the two states that look like
/// "don't disturb" but deliberately aren't.
/// </summary>
public class QuietModeTests
{
    [Theory]
    [InlineData(UserNotificationState.PresentationMode,     QuietReason.Presenting)]
    [InlineData(UserNotificationState.RunningD3dFullScreen, QuietReason.FullScreenGame)]
    [InlineData(UserNotificationState.Busy,                 QuietReason.FullScreenApp)]
    [InlineData(UserNotificationState.QuietTime,            QuietReason.FocusAssist)]
    public void States_that_mean_do_not_disturb_are_mapped(
        UserNotificationState state, QuietReason expected)
    {
        Assert.Equal(expected, SystemMonitor.MapQuiet(state));
    }

    [Fact]
    public void Normal_use_is_not_quiet()
    {
        Assert.Equal(QuietReason.None,
            SystemMonitor.MapQuiet(UserNotificationState.AcceptsNotifications));
    }

    [Fact]
    public void A_locked_machine_is_not_quiet()
    {
        // NotPresent means locked, screensavered or switched away. Nobody can be embarrassed by
        // a cat on a screen they aren't looking at, and the pet should be itself on their return.
        Assert.Equal(QuietReason.None,
            SystemMonitor.MapQuiet(UserNotificationState.NotPresent));
    }

    [Fact]
    public void A_screensaver_app_is_not_quiet()
    {
        Assert.Equal(QuietReason.None,
            SystemMonitor.MapQuiet(UserNotificationState.AppScreenSaver));
    }

    [Fact]
    public void An_unrecognised_state_defaults_to_not_quiet()
    {
        // A future Windows value must not silently mute the pet forever.
        Assert.Equal(QuietReason.None, SystemMonitor.MapQuiet((UserNotificationState)99));
    }

    [Fact]
    public void Querying_the_real_machine_returns_a_state_we_understand()
    {
        // Doesn't assert which state — that depends on what's on screen right now — only that
        // the P/Invoke succeeds and produces something the mapping handles.
        var state = Win32.GetUserNotificationState();
        var reason = SystemMonitor.MapQuiet(state);
        Assert.True(Enum.IsDefined(typeof(QuietReason), reason));
    }
}
