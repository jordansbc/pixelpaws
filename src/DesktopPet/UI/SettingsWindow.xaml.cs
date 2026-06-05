using System.Windows;
using System.Windows.Controls;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings    _settings;
    private readonly SettingsService _service;
    private readonly Action? _onChanged;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service, Action? onChanged = null)
    {
        InitializeComponent();
        _settings  = settings;
        _service   = service;
        _onChanged = onChanged;

        SpeedSlider.Value           = settings.Speed;
        WindowWalkingBox.IsChecked  = settings.EnableWindowWalking;
        CursorChaseBox.IsChecked    = settings.EnableCursorChase;
        SleepBox.IsChecked          = settings.EnableSleep;
        ScrollPlayBox.IsChecked     = settings.EnableScrollPlay;
        AutostartBox.IsChecked      = AutostartService.IsEnabled();

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

        _loaded = true;
        SizeBox.SelectionChanged += (_, _) => Apply();

        SpeedSlider.ValueChanged            += (_, _) => Apply();
        WindowWalkingBox.Checked            += (_, _) => Apply();
        WindowWalkingBox.Unchecked          += (_, _) => Apply();
        CursorChaseBox.Checked              += (_, _) => Apply();
        CursorChaseBox.Unchecked            += (_, _) => Apply();
        SleepBox.Checked                    += (_, _) => Apply();
        SleepBox.Unchecked                  += (_, _) => Apply();
        ScrollPlayBox.Checked               += (_, _) => Apply();
        ScrollPlayBox.Unchecked             += (_, _) => Apply();
        AutostartBox.Checked                += (_, _) => Apply();
        AutostartBox.Unchecked              += (_, _) => Apply();
        StretchIntervalBox.SelectionChanged += (_, _) => Apply();
    }

    private void Apply()
    {
        if (!_loaded) return;

        _settings.Speed                 = SpeedSlider.Value;
        _settings.EnableWindowWalking   = WindowWalkingBox.IsChecked == true;
        _settings.EnableCursorChase     = CursorChaseBox.IsChecked   == true;
        _settings.EnableSleep           = SleepBox.IsChecked          == true;
        _settings.EnableScrollPlay      = ScrollPlayBox.IsChecked     == true;

        bool autostart = AutostartBox.IsChecked == true;
        _settings.Autostart = autostart;
        AutostartService.Set(autostart);

        if (StretchIntervalBox.SelectedItem is ComboBoxItem ci &&
            int.TryParse(ci.Tag?.ToString(), out int mins))
            _settings.StretchIntervalMinutes = mins;

        if (SizeBox.SelectedItem is ComboBoxItem si &&
            double.TryParse(si.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double size))
            _settings.SizeScale = size;

        _service.Save();
        _onChanged?.Invoke();   // apply live (e.g. resize the cat)
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
