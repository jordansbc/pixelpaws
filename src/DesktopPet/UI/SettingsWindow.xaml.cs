using System.Windows;
using System.Windows.Controls;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings    _settings;
    private readonly SettingsService _service;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, SettingsService service)
    {
        InitializeComponent();
        _settings = settings;
        _service  = service;

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

        _loaded = true;

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

        _service.Save();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
