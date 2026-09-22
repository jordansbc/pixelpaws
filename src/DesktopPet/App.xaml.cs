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
    private HookWatchdog?    _hookWatchdog;
    private UpdateService?   _updateService;
    private DispatcherTimer? _stretchTimer;
    private DispatcherTimer? _stateSaveTimer;
    private DispatcherTimer? _quietWatchTimer;
    private bool _hiddenForQuiet;

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

        SpriteAnimator animator;
        try
        {
            animator = LoadPet(_settings.ActivePet);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load the cat's artwork.\n\n{ex.Message}", "PixelPaws",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _petWindow = new PetWindow();
        _petWindow.Show();

        DebugLog.Write($"PixelPaws {AppVersion.Display} starting. " +
                       $"DpiScale={_petWindow.DpiScale:0.###} " +
                       $"VirtualScreen=({SystemParameters.VirtualScreenLeft},{SystemParameters.VirtualScreenTop}) " +
                       $"{SystemParameters.VirtualScreenWidth}x{SystemParameters.VirtualScreenHeight}");

        // Persistent effects overlay (hearts/sparkles) — created here on the UI thread,
        // safely, NOT during the render loop.
        var overlay = new EffectsOverlay();
        overlay.Show();
        _petWindow.Effects = overlay;

        // Input monitors — installed on the UI thread so the hook callbacks run here.
        _keyboard = new KeyboardMonitor();
        _mouse    = new MouseMonitor();
        _system   = new SystemMonitor();

        // Windows drops low-level hooks silently if the UI thread ever stalls; this notices and
        // reinstalls them rather than leaving typing/scroll reactions dead for the session.
        _hookWatchdog = new HookWatchdog(_keyboard, _mouse, _system);

        var surfaceProvider = new SurfaceProvider(() => _petWindow!.Handle);
        var stateMachine    = new StateMachine(_settings, _system);
        _engine = new PetEngine(_petWindow, animator, surfaceProvider, stateMachine, _settings, _keyboard, _mouse, _system);
        _petWindow.TargetFps = _settings.TargetFps;
        _petWindow.Attach(_engine, _engine.Width, _engine.Height);

        // Watch for "should the cat be hidden right now?". Deliberately a slow timer rather than
        // engine work: when the answer is yes we detach the render loop completely, and the
        // engine then isn't ticking to notice when the answer changes back.
        _quietWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _quietWatchTimer.Tick += (_, _) => UpdateQuietHiding();
        _quietWatchTimer.Start();

        // Continuity: checkpoint where the cat is and how it feels, so an abrupt shutdown
        // (or a crash) still leaves something close to the truth on disk.
        _stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _stateSaveTimer.Tick += (_, _) => SavePetState();
        _stateSaveTimer.Start();

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

    private UpdateService Updater() => _updateService ??= new UpdateService(Http());

    private async void CheckForUpdates()
    {
        if (!_settings.EnableAutoUpdate) return;
        try
        {
            if (await Updater().IsUpdateAvailableAsync())
                _tray?.ShowUpdateAvailable();
        }
        catch { /* offline / rate-limited / no releases yet — ignore */ }
    }

    /// <summary>
    /// Apply an update. A git checkout hands off to update.bat (a developer wants their source
    /// pulled, not replaced by a release binary); a normal install downloads the published exe
    /// and lets a swap script put it in place once we have exited.
    /// </summary>
    private async void RunUpdate()
    {
        var updater = Updater();

        if (updater.IsSourceCheckout)
        {
            updater.RunUpdater();
            QuitApp();          // update.bat will rebuild and relaunch
            return;
        }

        _tray?.SetUpdateBusy(true);
        bool staged;
        try { staged = await updater.DownloadAndApplyAsync(); }
        catch { staged = false; }
        _tray?.SetUpdateBusy(false);

        if (staged)
        {
            QuitApp();          // the swap script is waiting for us to exit
        }
        else
        {
            MessageBox.Show(
                "Couldn't download the update. You can grab the latest build from the releases page instead.",
                "PixelPaws", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (updater.LatestUrl != null) OpenUrl(updater.LatestUrl);
        }
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
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

    /// <summary>
    /// Load a pet pack by folder name, falling back to the default cat if the configured one
    /// has gone missing (a user could have deleted a custom pack they were using).
    /// Art comes from <see cref="AssetSource"/>, so this works whether the assets are embedded
    /// in the exe or sitting on disk beside it.
    /// </summary>
    private static SpriteAnimator LoadPet(string pet)
    {
        foreach (string candidate in new[] { pet, "cat" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string dir = $"Assets/pets/{candidate}";
            if (!AssetSource.Exists($"{dir}/manifest.json")) continue;

            var manifest = PetManifest.Parse(AssetSource.ReadAllText($"{dir}/manifest.json"), dir);
            return new SpriteAnimator(manifest, AssetSource.ReadAllBytes($"{dir}/{manifest.Sheet}"));
        }

        throw new FileNotFoundException(
            $"No pet pack found for '{pet}', and the built-in cat is missing too.");
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }

        _settingsWindow = new SettingsWindow(_settings, _settingsService,
            onChanged: OnSettingsChanged, onForgetMemory: ForgetAiMemory,
            onTestAi: TestAiAsync, onCheckUpdate: () => Updater().DescribeCheckAsync());
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

        // Let the app actually go idle while paused: with the render handler detached, WPF stops
        // compositing a frame 60 times a second for a cat that isn't moving. Clear the
        // decorations first — once ticking stops, nothing else would take them down.
        if (paused)
        {
            _engine?.ClearDecorations();
            _petWindow?.SetRenderLoopEnabled(false);
        }
        else
        {
            // Coming back from pause must not override quiet-mode hiding.
            _hiddenForQuiet = false;
            UpdateQuietHiding();
            if (!_hiddenForQuiet) _petWindow?.SetRenderLoopEnabled(true);
        }
    }

    // ── AI companion ────────────────────────────────────────────────────────────
    // Off-switch guarantee: nothing here builds an HttpClient or makes a network call
    // unless EnableAiCompanion is true. Every entry point re-checks the flag.

    private void OnSettingsChanged()
    {
        _engine?.ApplySize(_settings.SizeScale);
        if (_petWindow != null) _petWindow.TargetFps = _settings.TargetFps;
        UpdateQuietHiding();
        ApplyAiState();   // key/persona/enabled may have changed in the Settings window
    }

    /// <summary>
    /// Hide the cat outright while the user is presenting, if they asked for that.
    ///
    /// Hiding detaches the render loop rather than just collapsing the image, which is what
    /// makes it free: being subscribed to WPF's per-frame Rendering event costs ~2.9% of a core
    /// even with a handler that does nothing, so a "hidden" pet that kept ticking would still
    /// be taxing the machine during exactly the presentation or game it was told to stay out of.
    /// </summary>
    private void UpdateQuietHiding()
    {
        if (_petWindow == null || _engine == null) return;
        if (_engine.Paused) return;      // pause already owns the render loop

        _system?.Poll();
        bool shouldHide = _settings.EnableQuietMode
                          && _settings.HideWhenPresenting
                          && _system?.ShouldNotDisturb == true;

        if (shouldHide == _hiddenForQuiet) return;
        _hiddenForQuiet = shouldHide;

        DebugLog.Write(shouldHide
            ? $"Hiding the cat ({_system?.Quiet}) and stopping the render loop."
            : "Showing the cat again and restarting the render loop.");

        if (shouldHide) _engine.ClearDecorations();
        _petWindow.SetPetVisible(!shouldHide);
        _petWindow.SetRenderLoopEnabled(!shouldHide);
    }

    /// <summary>Write the cat's position and mood into settings and save.</summary>
    private void SavePetState()
    {
        if (_engine == null || !_settings.RememberPetState) return;
        _engine.CaptureStateInto(_settings);
        _settingsService.Save();
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
        if (_engine.IsQuiet) return;   // presenting / gaming / DND — don't pipe up
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

    /// <summary>
    /// The one HttpClient in the app, shared by the AI providers, the cute tools and the
    /// updater. GitHub's API rejects requests without a User-Agent outright, and several
    /// keyless public APIs throttle them.
    /// </summary>
    private HttpClient Http() => _http ??= CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"PixelPaws/{AppVersion.Current} (+https://github.com/jordansbc/pixelpaws)");
        return http;
    }

    /// <summary>Build the chat service on first use. Only ever called when AI is enabled.</summary>
    private void EnsureAiBuilt()
    {
        if (_ai != null) return;
        _http ??= CreateHttp();
        _aiMemory ??= AiMemory.Load();
        IAiProvider provider = _settings.AiProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? new OllamaProvider(_http, _settings.OllamaUrl, _settings.OllamaModel)
            : new GeminiProvider(_http, _settings.AiApiKey, _settings.AiModel);

        var tools = new CuteTools(_system, _http);
        _ai = new AiChatService(_settings, provider, tools, _aiMemory);
    }

    /// <summary>Round-trip the configured brain so Settings can prove a key or a local server
    /// works, and say precisely what is wrong when it doesn't.</summary>
    private async Task<string> TestAiAsync(CancellationToken ct)
    {
        EnsureAiBuilt();
        if (_ai == null) return "Turn the AI companion on first.";

        var reply = await _ai.TestAsync(ct);
        return reply.Ok
            ? $"Works — the cat says: \u201c{reply.Text}\u201d"
            : reply.Failure switch
            {
                AiFailure.MissingKey    => "No API key set yet.",
                AiFailure.BadKey        => "That key was rejected. Double-check it.",
                AiFailure.QuotaExceeded => "Key works, but you're out of free quota right now.",
                AiFailure.UnknownModel  => $"Model not found. {reply.Detail}",
                AiFailure.Network       => $"Couldn't connect. {reply.Detail}",
                _                       => reply.Detail ?? "Something went wrong.",
            };
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
        _chatWindow.PlaceNear(_petWindow.PetLeft, _petWindow.PetTop, _engine.Width, _engine.Height);
        _chatWindow.Show();
    }

    private async void OnChatSubmitted(string text)
    {
        if (_ai == null || _engine == null) return;

        // Thinking state while we wait — gentle chat bob + an ellipsis bubble.
        _engine.ShowSpeech("\u2026", 60);
        _engine.RequestEmotion(PetState.Talk);

        // Type the reply out as it streams in. The emotion tag arrives at the very end, so
        // strip any partial "[" tail while typing rather than flashing it at the user.
        var typed = new System.Text.StringBuilder();
        void OnDelta(string chunk)
        {
            if (_engine == null) return;
            typed.Append(chunk);
            string shown = StripTrailingTag(typed.ToString());
            if (shown.Length > 0) _engine.ShowSpeech(shown, 60);
        }

        try
        {
            var reply = await _ai.SendAsync(text, CancellationToken.None, OnDelta);
            double secs = Math.Clamp(3 + reply.Text.Length * 0.06, 3, 12);
            _engine.ShowSpeech(reply.Text, secs);
            _engine.RequestEmotion(reply.Emotion);

            if (!reply.Ok)
                DebugLog.Write($"AI call failed: {reply.Failure} - {reply.Detail}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AI call threw: {ex}");
            _engine.ShowSpeech("*mew?*", 3);
        }
    }

    /// <summary>
    /// Hide a trailing emotion tag (complete or half-streamed) while text is still arriving,
    /// so the user never watches "[jo" appear and vanish.
    /// </summary>
    private static string StripTrailingTag(string s)
    {
        int open = s.LastIndexOf('[');
        if (open < 0) return s.TrimEnd();
        // Only treat it as a tag if nothing but letters/] follow it.
        for (int i = open + 1; i < s.Length; i++)
            if (!char.IsLetter(s[i]) && s[i] != ']') return s.TrimEnd();
        return s[..open].TrimEnd();
    }

    private void QuitApp()
    {
        SavePetState();
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _chatterTimer?.Stop();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SavePetState();
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _chatterTimer?.Stop();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
