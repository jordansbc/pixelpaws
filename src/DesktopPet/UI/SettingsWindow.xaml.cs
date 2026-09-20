using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
// UseWindowsForms adds a global `using System.Windows.Forms`, whose ComboBox collides with WPF's.
using ComboBox = System.Windows.Controls.ComboBox;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings     _settings;
    private readonly SettingsService _service;
    private readonly Action?         _onChanged;
    private readonly Action?         _onForgetMemory;
    private readonly Func<CancellationToken, Task<string>>? _onTestAi;
    private readonly Func<Task<string>>? _onCheckUpdate;
    private readonly string _initialPet;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service,
                          Action? onChanged = null, Action? onForgetMemory = null,
                          Func<CancellationToken, Task<string>>? onTestAi = null,
                          Func<Task<string>>? onCheckUpdate = null)
    {
        InitializeComponent();
        _settings       = settings;
        _service        = service;
        _onChanged      = onChanged;
        _onForgetMemory = onForgetMemory;
        _onTestAi       = onTestAi;
        _onCheckUpdate  = onCheckUpdate;
        _initialPet     = settings.ActivePet;

        SpeedSlider.Value            = settings.Speed;
        WindowWalkingBox.IsChecked   = settings.EnableWindowWalking;
        CursorChaseBox.IsChecked     = settings.EnableCursorChase;
        SleepBox.IsChecked           = settings.EnableSleep;
        ScrollPlayBox.IsChecked      = settings.EnableScrollPlay;
        MoodsBox.IsChecked           = settings.EnableMoods;
        SystemReactionsBox.IsChecked = settings.EnableSystemReactions;
        QuietModeBox.IsChecked       = settings.EnableQuietMode;
        HidePresentBox.IsChecked     = settings.HideWhenPresenting;
        AutoUpdateBox.IsChecked      = settings.EnableAutoUpdate;
        RememberStateBox.IsChecked   = settings.RememberPetState;
        AutostartBox.IsChecked       = AutostartService.IsEnabled();

        AiEnableBox.IsChecked    = settings.EnableAiCompanion;
        AiToolsBox.IsChecked     = settings.AiEnableTools;
        AiChatterBox.IsChecked   = settings.AiProactiveChatter;
        AiStreamingBox.IsChecked = settings.AiStreaming;
        AiHotkeyBox.IsChecked    = settings.AiHotkeyEnabled;
        HotkeyCaptureBox.Text    = FormatHotkey((ModifierKeys)settings.AiHotkeyModifiers, (Key)settings.AiHotkeyKey);
        AiMemoryBox.IsChecked    = settings.AiEnableMemory;
        AiKeyBox.Password        = settings.AiApiKey;
        AiModelBox.Text          = settings.AiModel;
        OllamaUrlBox.Text        = settings.OllamaUrl;
        OllamaModelBox.Text      = settings.OllamaModel;
        AiPersonaBox.Text        = settings.AiPersona;

        VersionText.Text = $"PixelPaws {AppVersion.Display}  •  settings in %AppData%\\PixelPaws";

        SelectByTag(StretchIntervalBox, settings.StretchIntervalMinutes.ToString(), fallbackIndex: 0);
        SelectByTag(AiProviderBox, settings.AiProvider, fallbackIndex: 0);
        SelectByTag(CatColorBox, settings.ActivePet, fallbackIndex: 0);

        // Size is a double, so match on closeness rather than exact text.
        foreach (ComboBoxItem item in SizeBox.Items)
        {
            if (double.TryParse(item.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double s)
                && Math.Abs(s - settings.SizeScale) < 0.05)
            {
                SizeBox.SelectedItem = item;
                break;
            }
        }
        if (SizeBox.SelectedItem == null) SizeBox.SelectedIndex = 1; // Medium

        UpdateProviderPanels();

        _loaded = true;
        WireUp();
    }

    /// <summary>Hook every control to <see cref="Apply"/>. Done after load so setting the
    /// initial values doesn't trigger a save-and-reapply storm on open.</summary>
    private void WireUp()
    {
        SpeedSlider.ValueChanged += (_, _) => Apply();

        foreach (var box in new[]
                 {
                     WindowWalkingBox, CursorChaseBox, SleepBox, ScrollPlayBox, MoodsBox,
                     SystemReactionsBox, QuietModeBox, HidePresentBox, AutoUpdateBox,
                     RememberStateBox, AutostartBox,
                     AiEnableBox, AiToolsBox, AiChatterBox, AiStreamingBox, AiHotkeyBox, AiMemoryBox,
                 })
        {
            box.Checked   += (_, _) => Apply();
            box.Unchecked += (_, _) => Apply();
        }

        foreach (var combo in new[] { SizeBox, CatColorBox, StretchIntervalBox, AiProviderBox })
            combo.SelectionChanged += (_, _) => Apply();

        AiKeyBox.PasswordChanged  += (_, _) => Apply();
        AiPersonaBox.TextChanged  += (_, _) => Apply();
        AiModelBox.TextChanged    += (_, _) => Apply();
        OllamaUrlBox.TextChanged  += (_, _) => Apply();
        OllamaModelBox.TextChanged+= (_, _) => Apply();
    }

    private static void SelectByTag(ComboBox box, string tag, int fallbackIndex)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = fallbackIndex;
    }

    private static string TagOf(ComboBox box) =>
        box.SelectedItem is ComboBoxItem ci ? ci.Tag?.ToString() ?? "" : "";

    private void UpdateProviderPanels()
    {
        bool ollama = TagOf(AiProviderBox).Equals("ollama", StringComparison.OrdinalIgnoreCase);
        GeminiPanel.Visibility = ollama ? Visibility.Collapsed : Visibility.Visible;
        OllamaPanel.Visibility = ollama ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void Apply()
    {
        if (!_loaded) return;

        _settings.Speed                 = SpeedSlider.Value;
        _settings.EnableWindowWalking   = WindowWalkingBox.IsChecked    == true;
        _settings.EnableCursorChase     = CursorChaseBox.IsChecked      == true;
        _settings.EnableSleep           = SleepBox.IsChecked            == true;
        _settings.EnableScrollPlay      = ScrollPlayBox.IsChecked       == true;
        _settings.EnableMoods           = MoodsBox.IsChecked            == true;
        _settings.EnableSystemReactions = SystemReactionsBox.IsChecked  == true;
        _settings.EnableQuietMode       = QuietModeBox.IsChecked        == true;
        _settings.HideWhenPresenting    = HidePresentBox.IsChecked      == true;
        _settings.EnableAutoUpdate      = AutoUpdateBox.IsChecked       == true;
        _settings.RememberPetState      = RememberStateBox.IsChecked    == true;

        _settings.EnableAiCompanion  = AiEnableBox.IsChecked    == true;
        _settings.AiEnableTools      = AiToolsBox.IsChecked     == true;
        _settings.AiProactiveChatter = AiChatterBox.IsChecked   == true;
        _settings.AiStreaming        = AiStreamingBox.IsChecked == true;
        _settings.AiHotkeyEnabled    = AiHotkeyBox.IsChecked    == true;
        _settings.AiEnableMemory     = AiMemoryBox.IsChecked    == true;
        _settings.AiApiKey           = AiKeyBox.Password;

        if (!string.IsNullOrWhiteSpace(AiPersonaBox.Text))   _settings.AiPersona   = AiPersonaBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(AiModelBox.Text))     _settings.AiModel     = AiModelBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(OllamaUrlBox.Text))   _settings.OllamaUrl   = OllamaUrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(OllamaModelBox.Text)) _settings.OllamaModel = OllamaModelBox.Text.Trim();

        _settings.AiProvider = TagOf(AiProviderBox) is { Length: > 0 } p ? p : "gemini";
        UpdateProviderPanels();

        bool autostart = AutostartBox.IsChecked == true;
        _settings.Autostart = autostart;
        AutostartService.Set(autostart);

        if (int.TryParse(TagOf(StretchIntervalBox), out int mins)) _settings.StretchIntervalMinutes = mins;

        if (double.TryParse(TagOf(SizeBox), System.Globalization.CultureInfo.InvariantCulture, out double size))
            _settings.SizeScale = size;

        string pet = TagOf(CatColorBox);
        if (pet.Length > 0)
        {
            _settings.ActivePet = pet;
            // The sprite sheet is loaded once at startup, so a color change only
            // takes effect after a restart — surface that hint when it changes.
            ColorRestartNote.Visibility =
                string.Equals(pet, _initialPet, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Collapsed : Visibility.Visible;
        }

        _service.Save();
        _onChanged?.Invoke();   // apply live (e.g. resize the cat)
    }

    /// <summary>Capture a new shortcut: record the first non-modifier key pressed with a modifier held.</summary>
    private void HotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses — wait for an actual key.
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;   // require at least one modifier so it's a safe global hotkey

        _settings.AiHotkeyModifiers = (int)mods;
        _settings.AiHotkeyKey       = (int)k;
        HotkeyCaptureBox.Text       = FormatHotkey(mods, k);
        Apply();
    }

    private static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onTestAi == null) return;
        TestButton.IsEnabled = false;
        TestResult.Text = "Asking the cat…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TestResult.Text = await _onTestAi(cts.Token);
        }
        catch (Exception ex)
        {
            TestResult.Text = ex.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onCheckUpdate == null) return;
        UpdateCheckButton.IsEnabled = false;
        UpdateResult.Text = "Checking…";
        try   { UpdateResult.Text = await _onCheckUpdate(); }
        catch (Exception ex) { UpdateResult.Text = ex.Message; }
        finally { UpdateCheckButton.IsEnabled = true; }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        // The crash log lives in %Temp%; open the folder rather than the file so it works
        // whether or not anything has ever been logged.
        try { Process.Start(new ProcessStartInfo(Path.GetTempPath()) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private void ForgetButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Make the cat forget everything it remembers about you and your past chats?",
            "Forget memory", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            _onForgetMemory?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
