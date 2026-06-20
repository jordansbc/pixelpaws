using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.Ai;
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
    private SystemMonitor?   _system;
    private UpdateService?   _updateService;
    private DispatcherTimer? _stretchTimer;

    // ── AI companion (built lazily, only when enabled) ──
    private HttpClient?      _http;
    private AiChatService?   _ai;
    private AiMemory?        _aiMemory;
    private ChatInputWindow? _chatWindow;
    private DispatcherTimer? _chatterTimer;
    private HotkeyService?   _hotkey;
    private readonly Random  _rng = new();

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
        _system   = new SystemMonitor();

        var surfaceProvider = new SurfaceProvider(() => _petWindow!.Handle);
        var stateMachine    = new StateMachine(_settings, _system);
        _engine = new PetEngine(_petWindow, animator, surfaceProvider, stateMachine, _settings, _keyboard, _mouse, _system);
        _petWindow.Attach(_engine, _engine.Width, _engine.Height);

        _tray = new TrayService(ShowSettings, OnPauseToggled, QuitApp, RunUpdate,
                                OnAiToggled, OpenChat, _settings.EnableAiCompanion);

        // AI companion: tapping the cat opens the chat box when it's enabled.
        _petWindow.ChatRequested += OpenChat;

        // Global hotkey (registered on the pet window's HWND, which now exists).
        _hotkey = new HotkeyService();
        _hotkey.Attach(_petWindow.Handle);
        _hotkey.Pressed += OpenChat;

        ApplyAiState();

        // Stretch timer — starts immediately if enabled.
        ResetStretchTimer();

        // Auto-update — quietly check GitHub, surface a tray prompt if a newer build exists.
        CheckForUpdates();
    }

    private async void CheckForUpdates()
    {
        if (!_settings.EnableAutoUpdate) return;
        try
        {
            _updateService ??= new UpdateService();
            if (await _updateService.IsUpdateAvailableAsync())
                _tray?.ShowUpdateAvailable();
        }
        catch { /* offline / no git — ignore */ }
    }

    private void RunUpdate()
    {
        (_updateService ??= new UpdateService()).RunUpdater();
        // update.bat will close this instance, rebuild, and relaunch.
        QuitApp();
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

        _settingsWindow = new SettingsWindow(_settings, _settingsService,
            onChanged: OnSettingsChanged, onForgetMemory: ForgetAiMemory);
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

    // ── AI companion ────────────────────────────────────────────────────────────
    // Off-switch guarantee: nothing here builds an HttpClient or makes a network call
    // unless EnableAiCompanion is true. Every entry point re-checks the flag.

    private void OnSettingsChanged()
    {
        _engine?.ApplySize(_settings.SizeScale);
        ApplyAiState();   // key/persona/enabled may have changed in the Settings window
    }

    /// <summary>Re-sync everything to the current AI on/off state. Rebuilds the service so a
    /// new key/model/persona takes effect, and tears the chat UI down when disabled.</summary>
    private void ApplyAiState()
    {
        bool on = _settings.EnableAiCompanion;
        if (_petWindow != null) _petWindow.AiTapOpensChat = on;
        _tray?.SetAiEnabled(on);
        _ai = null;   // force a fresh provider (picks up any new key/model) on next use
        if (on)
        {
            StartChatterTimer();
        }
        else
        {
            StopChatterTimer();
            _chatWindow?.Close();
            _chatWindow = null;
            _engine?.ClearSpeech();
        }
        ApplyHotkey();
    }

    /// <summary>Register or clear the global chat hotkey to match the current settings.</summary>
    private void ApplyHotkey()
    {
        if (_hotkey == null) return;
        if (_settings.EnableAiCompanion && _settings.AiHotkeyEnabled)
            _hotkey.Register((ModifierKeys)_settings.AiHotkeyModifiers, (Key)_settings.AiHotkeyKey);
        else
            _hotkey.Unregister();
    }

    private void StartChatterTimer()
    {
        if (_chatterTimer != null) return;
        _chatterTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(6) };
        _chatterTimer.Tick += OnChatterTick;
        _chatterTimer.Start();
    }

    private void StopChatterTimer()
    {
        _chatterTimer?.Stop();
        _chatterTimer = null;
    }

    /// <summary>Every so often, maybe let the cat say something unprompted — but only when the
    /// user is present, the cat is calm and quiet, and AI + chatter are both enabled.</summary>
    private async void OnChatterTick(object? sender, EventArgs e)
    {
        if (!_settings.EnableAiCompanion || !_settings.AiProactiveChatter) return;
        if (_engine == null || _engine.Paused || _engine.IsSpeaking) return;
        if (_chatWindow is { IsLoaded: true }) return;
        if (_system != null && _system.IdleSeconds > 60) return;  // don't talk to an empty chair
        if (_rng.NextDouble() > 0.30) return;                     // ~once every ~20 min on average

        EnsureAiBuilt();
        if (_ai == null) return;
        try
        {
            var reply = await _ai.ChatterAsync(BuildChatterContext(), CancellationToken.None);
            if (_engine == null || _engine.IsSpeaking) return;    // state may have changed mid-await
            double secs = Math.Clamp(3 + reply.Text.Length * 0.06, 3, 10);
            _engine.ShowSpeech(reply.Text, secs);
            _engine.RequestEmotion(reply.Emotion);
        }
        catch { /* stay quiet on failure */ }
    }

    private string BuildChatterContext()
    {
        _system?.Poll();
        int hour = DateTime.Now.Hour;
        string tod = hour < 6 ? "it's late at night" : hour < 12 ? "it's morning"
                   : hour < 17 ? "it's afternoon" : hour < 21 ? "it's evening" : "it's night";
        string app = _system?.Foreground switch
        {
            AppContextKind.Focus  => ", the user is working in a focus app",
            AppContextKind.Browse => ", the user is browsing the web",
            AppContextKind.Play   => ", the user is in something fun",
            _ => ""
        };
        string load = _system != null && _system.CpuLoad > 0.75 ? ", the computer is working hard" : "";
        string batt = _system != null && _system.OnBattery && _system.BatteryPercent is >= 0 and <= 20
                    ? ", the laptop battery is low" : "";
        return $"{tod}{app}{load}{batt}";
    }

    private void OnAiToggled(bool enabled)
    {
        _settings.EnableAiCompanion = enabled;
        _settingsService.Save();
        ApplyAiState();
    }

    /// <summary>Build the chat service on first use. Only ever called when AI is enabled.</summary>
    private void EnsureAiBuilt()
    {
        if (_ai != null) return;
        _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _aiMemory ??= AiMemory.Load();
        var provider = new GeminiProvider(_http, _settings.AiApiKey, _settings.AiModel);
        var tools    = new CuteTools(_system, _http);
        _ai = new AiChatService(_settings, provider, tools, _aiMemory);
    }

    /// <summary>Wipe everything the cat remembers (notes + chat history), on disk and in memory.</summary>
    private void ForgetAiMemory()
    {
        (_aiMemory ??= AiMemory.Load()).Clear();
        _ai = null;   // rebuild fresh with no seeded history next time
    }

    private void OpenChat()
    {
        if (!_settings.EnableAiCompanion || _petWindow == null || _engine == null) return;
        if (_chatWindow is { IsLoaded: true }) { _chatWindow.Activate(); return; }

        EnsureAiBuilt();
        _chatWindow = new ChatInputWindow();
        _chatWindow.Submitted += OnChatSubmitted;
        _chatWindow.Closed    += (_, _) => _chatWindow = null;
        _chatWindow.PlaceNear(_petWindow.Left, _petWindow.Top, _engine.Width, _engine.Height);
        _chatWindow.Show();
    }

    private async void OnChatSubmitted(string text)
    {
        if (_ai == null || _engine == null) return;

        // Thinking state while we wait — gentle chat bob + an ellipsis bubble.
        _engine.ShowSpeech("…", 30);
        _engine.RequestEmotion(PetState.Talk);
        try
        {
            var reply = await _ai.SendAsync(text, CancellationToken.None);
            double secs = Math.Clamp(3 + reply.Text.Length * 0.06, 3, 12);
            _engine.ShowSpeech(reply.Text, secs);
            _engine.RequestEmotion(reply.Emotion);
        }
        catch
        {
            _engine.ShowSpeech("*mew?*", 3);
        }
    }

    private void QuitApp()
    {
        _chatterTimer?.Stop();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _http?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _chatterTimer?.Stop();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _http?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
