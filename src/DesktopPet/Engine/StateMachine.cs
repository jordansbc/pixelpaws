using DesktopPet.Services;

namespace DesktopPet.Engine;

/// <summary>
/// Small helper that decides what the pet does next after an idle pause, and for how long.
/// Kept separate from <see cref="PetEngine"/> so the "personality" is easy to tune.
/// </summary>
public sealed class StateMachine
{
    private readonly Random _rng = new();
    private readonly AppSettings _settings;

    public StateMachine(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>How long (seconds) to linger in an idle pause before acting again.</summary>
    public double NextIdleDuration() => 0.8 + _rng.NextDouble() * 2.0;

    /// <summary>Pick the next active behaviour to start from idle.</summary>
    public PetState NextAction()
    {
        // Rough weights: Walk 45%, Chase 20%, Sleep 20%, Eat 15%.
        // Disabled behaviours re-roll as Walk.
        double roll = _rng.NextDouble();

        if (_settings.EnableCursorChase && roll < 0.20)
            return PetState.Chase;
        if (_settings.EnableSleep && roll < 0.40)
            return PetState.Sleep;
        if (roll < 0.55)
            return PetState.Eat;

        return PetState.Walk;
    }

    public double WalkDuration()  => 1.5 + _rng.NextDouble() * 3.0;
    public double SleepDuration() => 4.0 + _rng.NextDouble() * 6.0;
    public double ChaseDuration() => 2.5 + _rng.NextDouble() * 3.0;

    /// <summary>+1 (right) or -1 (left).</summary>
    public int RandomFacing() => _rng.Next(2) == 0 ? -1 : 1;

    public bool Chance(double p) => _rng.NextDouble() < p;
}
