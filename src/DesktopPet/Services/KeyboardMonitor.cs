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
        var hMod = Win32.GetModuleHandle(null);
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, hMod, 0);
    }

    private void Prune(long now)
    {
        long cutoff = now - _windowTicks;
        while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
            _timestamps.Dequeue();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
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
