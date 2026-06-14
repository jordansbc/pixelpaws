using DesktopPet.Services;

namespace DesktopPet.Engine;

/// <summary>
/// Decides what the pet does next after an idle pause, and for how long. Holds the
/// "personality": a slow energy/hunger model plus time-of-day and (optional)
/// system-awareness bias the weighted action roll. Kept separate from
/// <see cref="PetEngine"/> so the personality is easy to tune.
/// </summary>
public sealed class StateMachine
{
    private readonly Random _rng = new();
    private readonly AppSettings _settings;
    private readonly SystemMonitor? _system;

    // Slow-moving drives, 0..1. Energy falls while active, recovers while resting;
    // hunger rises over time and drops while eating.
    private double _energy = 0.7;
    private double _hunger = 0.2;

    private const double EnergyDrain   = 1.0 / 45.0;   // full -> empty over ~45s of hard activity
    private const double EnergyRecover = 1.0 / 35.0;   // empty -> full over ~35s of rest
    private const double EnergyDrift   = 1.0 / 90.0;   // idle drift toward a neutral 0.6
    private const double HungerFill    = 1.0 / 900.0;  // peckish after ~15 min
    private const double HungerEat     = 1.0 / 6.0;    // a meal satisfies in ~6s

    public StateMachine(AppSettings settings, SystemMonitor? system = null)
    {
        _settings = settings;
        _system   = system;
    }

    public double Energy => _energy;

    /// <summary>Evolve the drives each frame based on what the cat is currently doing.</summary>
    public void Tick(double dt, PetState state)
    {
        switch (state)
        {
            case PetState.Zoomies:
            case PetState.Chase:
            case PetState.Hunt:
            case PetState.Pounce:
            case PetState.Bat:
            case PetState.Walk:
                _energy -= EnergyDrain * dt;
                break;
            case PetState.Sleep:
            case PetState.Yawn:
            case PetState.Loaf:
                _energy += EnergyRecover * dt;
                break;
            default:
                _energy += (0.6 - _energy) * EnergyDrift * dt;
                break;
        }

        if (state == PetState.Eat) _hunger -= HungerEat * dt;
        else                        _hunger += HungerFill * dt;

        _energy = Math.Clamp(_energy, 0, 1);
        _hunger = Math.Clamp(_hunger, 0, 1);
    }

    /// <summary>How long (seconds) to linger in an idle pause before acting again.</summary>
    public double NextIdleDuration() => 0.8 + _rng.NextDouble() * 2.0;

    /// <summary>Pick the next active behaviour to start from idle, weighted by mood,
    /// time of day, and (if enabled) what the computer is doing.</summary>
    public PetState NextAction()
    {
        bool moods = _settings.EnableMoods;
        bool sys   = _settings.EnableSystemReactions && _system != null;

        int hour = DateTime.Now.Hour;
        bool night   = moods && (hour >= 22 || hour < 6);
        bool evening = moods && hour >= 18 && hour < 22;

        double energy = moods ? _energy : 0.6;
        double hunger = moods ? _hunger : 0.3;

        double cpu = sys ? _system!.CpuLoad : 0.0;
        var fg     = sys ? _system!.Foreground : AppContextKind.Other;
        bool lowBatt = sys && _system!.OnBattery && _system.BatteryPercent is (> 0 and < 20);
        bool focus = fg == AppContextKind.Focus;
        bool playful = fg is AppContextKind.Play or AppContextKind.Browse;

        // restful when low energy / night / low battery; lively when high energy / load / play app.
        double rest   = 1.0 + (1.0 - energy) * 2.5 + (night ? 3.0 : 0) + (evening ? 1.0 : 0)
                            + (lowBatt ? 2.0 : 0) + (focus ? 1.0 : 0);
        double active = 1.0 + energy * 2.0 + cpu * 1.5 + (playful ? 1.5 : 0)
                            - (night ? 0.7 : 0) - (focus ? 0.6 : 0) - (lowBatt ? 0.6 : 0);
        active = Math.Max(0.2, active);

        var w = new List<(PetState s, double weight)>
        {
            (PetState.Gift,    0.10),
            (PetState.Zoomies, 0.9 * active),
            (PetState.Chase,   _settings.EnableCursorChase ? 1.0 * active : 0),
            (PetState.Groom,   1.2),
            (PetState.Loaf,    1.0 * rest),
            (PetState.Yawn,    _settings.EnableSleep ? 0.9 * rest : 0),  // yawn -> sleep
            (PetState.Eat,     0.8 + hunger * 5.0),
            (PetState.Walk,    2.0),
        };

        double total = 0;
        foreach (var (_, weight) in w) total += Math.Max(0, weight);
        double roll = _rng.NextDouble() * total;
        foreach (var (s, weight) in w)
        {
            roll -= Math.Max(0, weight);
            if (roll <= 0) return s;
        }
        return PetState.Walk;
    }

    public double WalkDuration()    => 1.5 + _rng.NextDouble() * 3.0;
    public double SleepDuration()   => 4.0 + _rng.NextDouble() * 6.0;
    public double ChaseDuration()   => 2.5 + _rng.NextDouble() * 3.0;
    public double ZoomiesDuration() => 1.4 + _rng.NextDouble() * 1.8;
    public double GroomDuration()   => 2.5 + _rng.NextDouble() * 3.0;
    public double LoafDuration()    => 5.0 + _rng.NextDouble() * 6.0;

    /// <summary>+1 (right) or -1 (left).</summary>
    public int RandomFacing() => _rng.Next(2) == 0 ? -1 : 1;

    public bool Chance(double p) => _rng.NextDouble() < p;
}
