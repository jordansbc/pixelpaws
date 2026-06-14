using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPet.Services;

/// <summary>What kind of app is in the foreground, so the pet can read the room.</summary>
public enum AppContextKind { Other, Focus, Browse, Play }

/// <summary>
/// Lightweight, poll-on-demand view of the machine's state: how long the user has
/// been away, overall CPU load, battery, and what kind of app is in front. Values are
/// recomputed at most every <see cref="SampleInterval"/> seconds so it stays cheap to
/// read every frame.
/// </summary>
public sealed class SystemMonitor
{
    private const double SampleInterval = 2.0;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _nextSample;

    // CPU sampling state (delta of system times between samples).
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _haveCpuBaseline;

    public double IdleSeconds { get; private set; }
    public double CpuLoad { get; private set; }          // 0..1
    public int BatteryPercent { get; private set; } = -1; // -1 = no battery / unknown
    public bool OnBattery { get; private set; }
    public AppContextKind Foreground { get; private set; } = AppContextKind.Other;

    /// <summary>Refresh the cached values if the sample interval has elapsed.</summary>
    public void Poll()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now < _nextSample) return;
        _nextSample = now + SampleInterval;

        SampleIdle();
        SampleCpu();
        SampleBattery();
        SampleForeground();
    }

    private void SampleIdle()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info))
        {
            uint ms = unchecked((uint)Environment.TickCount - info.dwTime);
            IdleSeconds = ms / 1000.0;
        }
    }

    private void SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
        ulong i = ToU64(idle), k = ToU64(kernel), u = ToU64(user);
        if (_haveCpuBaseline)
        {
            ulong di = i - _lastIdle, dk = k - _lastKernel, du = u - _lastUser;
            ulong total = dk + du;            // kernel time already includes idle
            CpuLoad = total == 0 ? 0 : Math.Clamp(1.0 - (double)di / total, 0, 1);
        }
        _lastIdle = i; _lastKernel = k; _lastUser = u;
        _haveCpuBaseline = true;
    }

    private void SampleBattery()
    {
        try
        {
            var p = SystemInformation.PowerStatus;
            OnBattery = p.PowerLineStatus == PowerLineStatus.Offline;
            float life = p.BatteryLifePercent;             // 1.0 == unknown/full
            BatteryPercent = (life >= 0 && life <= 1) ? (int)Math.Round(life * 100) : -1;
            if (p.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
                BatteryPercent = -1;
        }
        catch { BatteryPercent = -1; OnBattery = false; }
    }

    private static readonly string[] FocusApps =
        { "code", "devenv", "rider64", "rider", "idea64", "pycharm64", "sublime_text",
          "notepad++", "notepad", "winword", "excel", "powerpnt", "obsidian", "acrord32" };
    private static readonly string[] BrowseApps =
        { "chrome", "msedge", "firefox", "brave", "opera", "arc", "vivaldi" };
    private static readonly string[] PlayApps =
        { "steam", "steamwebhelper", "epicgameslauncher", "vlc", "spotify", "wmplayer",
          "discord", "leagueclient", "csgo", "valorant", "minecraft" };

    private void SampleForeground()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) { Foreground = AppContextKind.Other; return; }
            GetWindowThreadProcessId(h, out uint pid);
            string name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
            if (Array.IndexOf(FocusApps, name) >= 0) Foreground = AppContextKind.Focus;
            else if (Array.IndexOf(BrowseApps, name) >= 0) Foreground = AppContextKind.Browse;
            else if (Array.IndexOf(PlayApps, name) >= 0) Foreground = AppContextKind.Play;
            else Foreground = AppContextKind.Other;
        }
        catch { Foreground = AppContextKind.Other; }
    }

    private static ulong ToU64(FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    // ── native ──────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public int dwLowDateTime; public int dwHighDateTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
