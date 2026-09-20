using System.Windows.Threading;

namespace DesktopPet.Services;

/// <summary>
/// Keeps the global keyboard and mouse hooks alive.
///
/// Both hooks are WH_*_LL hooks installed on the UI thread, which also runs the 60 fps render
/// loop and enumerates every top-level window four times a second. If that thread ever overruns
/// Windows' <c>LowLevelHooksTimeout</c> (300 ms by default) the OS quietly unhooks us: the handle
/// stays non-zero, nothing throws, and no callback ever arrives again. Typing and scroll
/// reactions would simply stop working for the rest of the session.
///
/// There is no API to ask whether a hook is still installed, so we infer it: if Windows says the
/// user has been giving input in the last couple of seconds but one of our hooks has seen nothing
/// for <see cref="SilenceSeconds"/>, that hook is dead and we reinstall it.
/// </summary>
public sealed class HookWatchdog : IDisposable
{
    /// <summary>A hook silent for this long while the user is active is presumed dead.</summary>
    private const double SilenceSeconds = 20.0;

    /// <summary>How recently the user must have given input for silence to be suspicious.</summary>
    private const double ActiveWithinSeconds = 2.0;

    private readonly KeyboardMonitor? _keyboard;
    private readonly MouseMonitor?    _mouse;
    private readonly SystemMonitor?   _system;
    private readonly DispatcherTimer  _timer;

    public HookWatchdog(KeyboardMonitor? keyboard, MouseMonitor? mouse, SystemMonitor? system)
    {
        _keyboard = keyboard;
        _mouse    = mouse;
        _system   = system;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_system == null) return;

        _system.Poll();
        if (_system.IdleSeconds > ActiveWithinSeconds) return;   // nobody typing or moving: proves nothing

        // A re-arm is cheap and harmless, so a false positive (e.g. the user mousing for a long
        // stretch without touching the keyboard) costs nothing but a reinstall.
        if (_keyboard != null && _keyboard.SecondsSinceLastEvent > SilenceSeconds)
        {
            DebugLog.Write("Keyboard hook went silent while the user was active - reinstalling.");
            _keyboard.Rearm();
        }

        if (_mouse != null && _mouse.SecondsSinceLastEvent > SilenceSeconds)
        {
            DebugLog.Write("Mouse hook went silent while the user was active - reinstalling.");
            _mouse.Rearm();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
