using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DesktopPet;

public partial class App : Application
{
    private Mutex?           _singleInstance;
    private SettingsService  _settingsService = new();
    private AppSettings      _settings        = new();
    private PetWindow?       _petWindow;
    private PetEngine?       _engine;
    private TrayService?     _tray;
    private SettingsWindow?  _settingsWindow;
    private KeyboardMonitor? _keyboard;
    private MouseMonitor?    _mouse;
    private DispatcherTimer? _stretchTimer;

    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "pixelpaws_crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);


        // Crash diagnostics — capture any unhandled exception with its stack trace.
        // Mark handled so a single stray exception can't kill the user's pet.
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DISPATCHER\n{args.Exception}\n\n"); } catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DOMAIN\n{args.ExceptionObject}\n\n"); } catch { }
        };

        _singleInstance = new Mutex(initiallyOwned: true, "PixelPaws.SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        _settings = _settingsService.Load();

        string petDir = ResolvePetDir(_settings.ActivePet);
        string manifestPath = Path.Combine(petDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            MessageBox.Show($"Pet assets not found:\n{manifestPath}", "PixelPaws",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var manifest = PetManifest.Load(manifestPath);
        var animator = new SpriteAnimator(manifest, petDir);

        _petWindow = new PetWindow();
        _petWindow.Show();

        // Persistent effects overlay (hearts/sparkles) — created here on the UI thread,
        // safely, NOT during the render loop.
        var overlay = new EffectsOverlay();
        overlay.Show();
        _petWindow.Effects = overlay;

        // Input monitors — installed on the UI thread so the hook callbacks run here.
        _keyboard = new KeyboardMonitor();
        _mouse    = new MouseMonitor();

        var surfaceProvider = new SurfaceProvider(() => _petWindow!.Handle);
        var stateMachine    = new StateMachine(_settings);
        _engine = new PetEngine(_petWindow, animator, surfaceProvider, stateMachine, _settings, _keyboard, _mouse);
        _petWindow.Attach(_engine, _engine.Width, _engine.Height);

        _tray = new TrayService(ShowSettings, OnPauseToggled, QuitApp);

        // Stretch timer — starts immediately if enabled.
        ResetStretchTimer();
    }

    private void ResetStretchTimer()
    {
        _stretchTimer?.Stop();
        _stretchTimer = null;

        if (_settings.StretchIntervalMinutes <= 0) return;

        _stretchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.StretchIntervalMinutes)
        };
        _stretchTimer.Tick += (_, _) => TriggerStretch();
        _stretchTimer.Start();
    }

    private void TriggerStretch()
    {
        _engine?.RequestStretch();

        // Show the cute notification
        var notification = new StretchNotification();
        notification.Show();
    }

    private static string ResolvePetDir(string pet)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "pets", pet),
            Path.Combine(AppContext.BaseDirectory, "Assets", "pets", "cat"),
        };
        foreach (var c in candidates)
            if (Directory.Exists(c)) return c;
        return candidates[0];
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }

        _settingsWindow = new SettingsWindow(_settings, _settingsService);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ResetStretchTimer(); // interval may have changed
        };
        _settingsWindow.Show();
    }

    private void OnPauseToggled(bool paused)
    {
        if (_engine != null) _engine.Paused = paused;
        _tray?.SetPaused(paused);
    }

    private void QuitApp()
    {
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
