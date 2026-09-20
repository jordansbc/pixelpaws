using System.Diagnostics;
using DesktopPet.Native;

namespace DesktopPet.Services;

/// <summary>
/// Low-level global keyboard hook. Tracks keystroke rate so the pet can react to typing.
/// Must be created on the UI thread (which has a message pump for the hook callback).
/// </summary>
public sealed class KeyboardMonitor : IDisposable
{
    // Keep a reference so the GC never collects the delegate while the hook is alive.
    private readonly Win32.LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    private readonly Queue<long> _timestamps = new();        // Stopwatch ticks of recent keydowns
    private readonly long _windowTicks = Stopwatch.Frequency; // 1-second rolling window
    private readonly object _gate = new();

    // Liveness: Windows silently drops a low-level hook whose thread overran
    // LowLevelHooksTimeout, without changing the handle or reporting an error. The only signal
    // is that callbacks stop arriving, so track when one last did. See HookWatchdog.
    private long _lastCallback = Stopwatch.GetTimestamp();

    /// <summary>Seconds since this hook last received any callback from Windows.</summary>
    public double SecondsSinceLastEvent =>
        (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastCallback)) / (double)Stopwatch.Frequency;

    /// <summary>
    /// Keystrokes per second over the last rolling second. Computed on read and pruned by
    /// time, so it decays to 0 once you stop typing (this is what stops the typing animation).
    /// </summary>
    public float KeysPerSecond
    {
        get
        {
            lock (_gate)
            {
                Prune(Stopwatch.GetTimestamp());
                return _timestamps.Count;   // window is exactly 1s, so count == keys/sec
            }
        }
    }

    public bool IsTyping     => KeysPerSecond > 1.0f;
    public bool IsTypingFast => KeysPerSecond > 7.5f;

    public KeyboardMonitor()
    {
        _proc = HookCallback;
        Install();
    }

    private void Install()
    {
        var hMod = Win32.GetModuleHandle(null);
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, hMod, 0);
        Interlocked.Exchange(ref _lastCallback, Stopwatch.GetTimestamp());
    }

    /// <summary>Tear the hook down and install it again. Safe to call at any time — the worst a
    /// needless re-arm costs is a keystroke or two of missed rate data.</summary>
    public void Rearm()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        Install();
    }

    private void Prune(long now)
    {
        long cutoff = now - _windowTicks;
        while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
            _timestamps.Dequeue();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Stamp on every callback, not just key-downs: key-ups prove the hook is alive too.
        Interlocked.Exchange(ref _lastCallback, Stopwatch.GetTimestamp());

        if (nCode >= 0 && (wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN))
        {
            long now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                _timestamps.Enqueue(now);
                Prune(now);
            }
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
