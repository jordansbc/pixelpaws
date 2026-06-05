using System.Windows;
using System.Windows.Threading;

namespace DesktopPet.UI;

public partial class StretchNotification : Window
{
    private readonly DispatcherTimer _autoDismiss;

    public StretchNotification()
    {
        InitializeComponent();

        // Position at top-center of primary monitor work area
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width  - Width)  / 2;
        Top  = work.Top  + 24;

        _autoDismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _autoDismiss.Tick += (_, _) => { _autoDismiss.Stop(); Close(); };
        _autoDismiss.Start();

        // Slide-in animation via opacity
        Opacity = 0;
        Loaded += (_, _) => FadeIn();
    }

    private void FadeIn()
    {
        int tick = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        t.Tick += (_, _) =>
        {
            Opacity = Math.Min(1, tick / 12.0);
            if (++tick >= 12) t.Stop();
        };
        t.Start();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
