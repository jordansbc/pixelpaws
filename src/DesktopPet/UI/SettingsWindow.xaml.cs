using System.Windows;
using System.Windows.Controls;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings    _settings;
    private readonly SettingsService _service;
    private readonly Action? _onChanged;
    private readonly string _initialPet;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service, Action? onChanged = null)
    {
        InitializeComponent();
        _settings   = settings;
        _service    = service;
        _onChanged  = onChanged;
        _initialPet = settings.ActivePet;

        SpeedSlider.Value           = settings.Speed;
        WindowWalkingBox.IsChecked  = settings.EnableWindowWalking;
        CursorChaseBox.IsChecked    = settings.EnableCursorChase;
        SleepBox.IsChecked          = settings.EnableSleep;
        ScrollPlayBox.IsChecked     = settings.EnableScrollPlay;
        MoodsBox.IsChecked          = settings.EnableMoods;
        SystemReactionsBox.IsChecked= settings.EnableSystemReactions;
        AutoUpdateBox.IsChecked     = settings.EnableAutoUpdate;
        AutostartBox.IsChecked      = AutostartService.IsEnabled();

        AiEnableBox.IsChecked       = settings.EnableAiCompanion;
        AiToolsBox.IsChecked        = settings.AiEnableTools;
        AiKeyBox.Password           = settings.AiApiKey;
        AiPersonaBox.Text           = settings.AiPersona;

        // Select the matching ComboBox item for stretch interval
        foreach (ComboBoxItem item in StretchIntervalBox.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int val) && val == settings.StretchIntervalMinutes)
            {
                StretchIntervalBox.SelectedItem = item;
                break;
            }
        }
        if (StretchIntervalBox.SelectedItem == null)
            StretchIntervalBox.SelectedIndex = 0;

        // Select the closest size option.
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

        // Select the matching cat color.
        foreach (ComboBoxItem item in CatColorBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), settings.ActivePet, StringComparison.OrdinalIgnoreCase))
            {
                CatColorBox.SelectedItem = item;
                break;
            }
        }
        if (CatColorBox.SelectedItem == null) CatColorBox.SelectedIndex = 0; // Gray

        _loaded = true;
        SizeBox.SelectionChanged += (_, _) => Apply();
        CatColorBox.SelectionChanged += (_, _) => Apply();

        SpeedSlider.ValueChanged            += (_, _) => Apply();
        WindowWalkingBox.Checked            += (_, _) => Apply();
        WindowWalkingBox.Unchecked          += (_, _) => Apply();
        CursorChaseBox.Checked              += (_, _) => Apply();
        CursorChaseBox.Unchecked            += (_, _) => Apply();
        SleepBox.Checked                    += (_, _) => Apply();
        SleepBox.Unchecked                  += (_, _) => Apply();
        ScrollPlayBox.Checked               += (_, _) => Apply();
        ScrollPlayBox.Unchecked             += (_, _) => Apply();
        MoodsBox.Checked                    += (_, _) => Apply();
        MoodsBox.Unchecked                  += (_, _) => Apply();
        SystemReactionsBox.Checked          += (_, _) => Apply();
        SystemReactionsBox.Unchecked        += (_, _) => Apply();
        AutoUpdateBox.Checked               += (_, _) => Apply();
        AutoUpdateBox.Unchecked             += (_, _) => Apply();
        AutostartBox.Checked                += (_, _) => Apply();
        AutostartBox.Unchecked              += (_, _) => Apply();
        StretchIntervalBox.SelectionChanged += (_, _) => Apply();

        AiEnableBox.Checked                 += (_, _) => Apply();
        AiEnableBox.Unchecked               += (_, _) => Apply();
        AiToolsBox.Checked                  += (_, _) => Apply();
        AiToolsBox.Unchecked                += (_, _) => Apply();
        AiKeyBox.PasswordChanged            += (_, _) => Apply();
        AiPersonaBox.TextChanged            += (_, _) => Apply();
    }

    private void Apply()
    {
        if (!_loaded) return;

        _settings.Speed                 = SpeedSlider.Value;
        _settings.EnableWindowWalking   = WindowWalkingBox.IsChecked == true;
        _settings.EnableCursorChase     = CursorChaseBox.IsChecked   == true;
        _settings.EnableSleep           = SleepBox.IsChecked          == true;
        _settings.EnableScrollPlay      = ScrollPlayBox.IsChecked     == true;
        _settings.EnableMoods           = MoodsBox.IsChecked          == true;
        _settings.EnableSystemReactions = SystemReactionsBox.IsChecked == true;
        _settings.EnableAutoUpdate      = AutoUpdateBox.IsChecked      == true;

        _settings.EnableAiCompanion     = AiEnableBox.IsChecked        == true;
        _settings.AiEnableTools         = AiToolsBox.IsChecked         == true;
        _settings.AiApiKey              = AiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(AiPersonaBox.Text))
            _settings.AiPersona         = AiPersonaBox.Text.Trim();

        bool autostart = AutostartBox.IsChecked == true;
        _settings.Autostart = autostart;
        AutostartService.Set(autostart);

        if (StretchIntervalBox.SelectedItem is ComboBoxItem ci &&
            int.TryParse(ci.Tag?.ToString(), out int mins))
            _settings.StretchIntervalMinutes = mins;

        if (SizeBox.SelectedItem is ComboBoxItem si &&
            double.TryParse(si.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double size))
            _settings.SizeScale = size;

        if (CatColorBox.SelectedItem is ComboBoxItem ci2 && ci2.Tag is string pet)
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
