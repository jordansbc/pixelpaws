using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>The personality model: drives stay in range, and continuity survives a restart.</summary>
public class StateMachineTests
{
    private static AppSettings Settings() => new();

    [Fact]
    public void Drives_stay_within_range_under_sustained_activity()
    {
        var sm = new StateMachine(Settings());
        for (int i = 0; i < 10_000; i++) sm.Tick(0.1, PetState.Zoomies);

        Assert.InRange(sm.Energy, 0, 1);
        Assert.InRange(sm.Hunger, 0, 1);
    }

    [Fact]
    public void Drives_stay_within_range_under_sustained_rest()
    {
        var sm = new StateMachine(Settings());
        for (int i = 0; i < 10_000; i++) sm.Tick(0.1, PetState.Sleep);

        Assert.InRange(sm.Energy, 0, 1);
        Assert.InRange(sm.Hunger, 0, 1);
    }

    [Fact]
    public void Running_about_tires_the_cat_and_resting_restores_it()
    {
        var active = new StateMachine(Settings());
        var rested = new StateMachine(Settings());
        for (int i = 0; i < 100; i++)
        {
            active.Tick(0.1, PetState.Zoomies);
            rested.Tick(0.1, PetState.Sleep);
        }

        Assert.True(active.Energy < rested.Energy);
    }

    [Fact]
    public void Eating_reduces_hunger()
    {
        var sm = new StateMachine(Settings());
        for (int i = 0; i < 600; i++) sm.Tick(0.1, PetState.Idle);   // get peckish
        double before = sm.Hunger;

        for (int i = 0; i < 60; i++) sm.Tick(0.1, PetState.Eat);
        Assert.True(sm.Hunger < before);
    }

    [Fact]
    public void Mood_survives_a_restart()
    {
        var settings = Settings();
        var first = new StateMachine(settings);
        for (int i = 0; i < 300; i++) first.Tick(0.1, PetState.Zoomies);

        first.CaptureInto(settings);
        var second = new StateMachine(settings);

        Assert.Equal(first.Energy, second.Energy, 6);
        Assert.Equal(first.Hunger, second.Hunger, 6);
    }

    [Fact]
    public void Mood_resets_when_continuity_is_switched_off()
    {
        var settings = Settings();
        settings.LastEnergy = 0.01;
        settings.LastHunger = 0.99;
        settings.RememberPetState = false;

        var sm = new StateMachine(settings);
        Assert.NotEqual(0.01, sm.Energy);
    }

    [Fact]
    public void Saved_drives_outside_the_valid_range_are_clamped_not_trusted()
    {
        // A hand-edited or corrupted settings file must not put the cat into an impossible mood.
        var settings = Settings();
        settings.LastEnergy = 99;
        settings.LastHunger = 42;

        var sm = new StateMachine(settings);
        Assert.InRange(sm.Energy, 0, 1);
        Assert.InRange(sm.Hunger, 0, 1);
    }

    [Fact]
    public void NextAction_only_ever_returns_states_the_engine_can_handle()
    {
        var sm = new StateMachine(Settings());
        for (int i = 0; i < 2_000; i++)
        {
            var next = sm.NextAction();
            Assert.True(Enum.IsDefined(typeof(PetState), next), $"unknown state {next}");
            // The idle roll should never pick states driven by external input or physics.
            Assert.NotEqual(PetState.Drag, next);
            Assert.NotEqual(PetState.Fall, next);
            Assert.NotEqual(PetState.Pet, next);
        }
    }

    [Fact]
    public void Cursor_chasing_is_never_chosen_when_the_user_turned_it_off()
    {
        var settings = Settings();
        settings.EnableCursorChase = false;

        var sm = new StateMachine(settings);
        for (int i = 0; i < 2_000; i++)
            Assert.NotEqual(PetState.Chase, sm.NextAction());
    }

    [Fact]
    public void Naps_are_never_chosen_when_the_user_turned_them_off()
    {
        var settings = Settings();
        settings.EnableSleep = false;

        var sm = new StateMachine(settings);
        for (int i = 0; i < 2_000; i++)
            Assert.NotEqual(PetState.Yawn, sm.NextAction());   // Yawn is the gateway into Sleep
    }
}
