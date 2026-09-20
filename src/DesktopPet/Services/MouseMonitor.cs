using System.Diagnostics;
using DesktopPet.Native;

namespace DesktopPet.Services;

/// <summary>
/// Low-level global mouse hook. Accumulates wheel movement so the pet can react to scrolling
/// (e.g. unrolling toilet paper). Must be created on the UI thread. Never consumes the event —
/// it always forwards via CallNextHookEx so normal scrolling keeps working everywhere.
/// </summary>
public sealed class MouseMonitor : IDisposable
{
    private readonly Win32.LowLevelKeyboardProc _proc; // identical (int, IntPtr, IntPtr) signature
    private IntPtr _hook;
    private int _accumulated;          // sum of |wheel delta| since last read (120 per notch)
    private readonly object _gate = new();

    // Liveness: Windows silently drops a low-level hook whose thread overran
    // LowLevelHooksTimeout, without changing the handle or reporting an error. The only signal
    // is that callbacks stop arriving, so track when one last did. See HookWatchdog.
    private long _lastCallback = Stopwatch.GetTimestamp();

    /// <summary>Seconds since this hook last received any callback from Windows.</summary>
    public double SecondsSinceLastEvent =>
        (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastCallback)) / (double)Stopwatch.Frequency;

    public MouseMonitor()
    {
        _proc = HookCallback;
        Install();
    }

    private void Install()
    {
        var hMod = Win32.GetModuleHandle(null);
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, hMod, 0);
        Interlocked.Exchange(ref _lastCallback, Stopwatch.GetTimestamp());
    }

    /// <summary>Tear the hook down and install it again. Safe to call at any time — the worst a
    /// needless re-arm costs is a scroll notch or two.</summary>
    public void Rearm()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        Install();
    }

    /// <summary>Notches scrolled since the last call (absolute magnitude), then resets.</summary>
    public double TakeScrollNotches()
    {
        lock (_gate)
        {
            double notches = _accumulated / 120.0;
            _accumulated = 0;
            return notches;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Stamp on every callback, not just wheel events: plain mouse moves prove liveness too,
        // and they arrive constantly whenever the user is actually at the machine.
        Interlocked.Exchange(ref _lastCallback, Stopwatch.GetTimestamp());

        if (nCode >= 0 && wParam == (IntPtr)Win32.WM_MOUSEWHEEL)
        {
            int delta = Win32.ReadWheelDelta(lParam);
            lock (_gate) { _accumulated += Math.Abs(delta); }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
