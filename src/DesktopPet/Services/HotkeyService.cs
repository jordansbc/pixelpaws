using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPet.Services;

/// <summary>
/// Registers a single user-configurable global hotkey on a host window and raises
/// <see cref="Pressed"/> when it fires. Uses RegisterHotKey + a WndProc hook on the
/// pet window's HWND — no extra low-level keyboard hook needed.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId  = 0xB001;

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event Action? Pressed;

    /// <summary>Hook the host window so WM_HOTKEY messages reach us.</summary>
    public void Attach(IntPtr hwnd)
    {
        _hwnd   = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>Register (replacing any prior binding). Returns false if the combo is taken or invalid.</summary>
    public bool Register(ModifierKeys mods, Key key)
    {
        Unregister();
        if (_hwnd == IntPtr.Zero || key == Key.None) return false;

        uint fs = MOD_NOREPEAT;
        if (mods.HasFlag(ModifierKeys.Alt))     fs |= MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Control)) fs |= MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Shift))   fs |= MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) fs |= MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(_hwnd, HotkeyId, fs, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
