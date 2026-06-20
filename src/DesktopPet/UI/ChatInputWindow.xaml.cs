using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopPet.UI;

/// <summary>
/// A tiny focusable text box that pops up near the cat so you can talk to it. Unlike the
/// effects overlay this window is NOT click-through — it needs keyboard focus. Enter submits,
/// Escape (or clicking away) dismisses it.
/// </summary>
public partial class ChatInputWindow : Window
{
    public event Action<string>? Submitted;

    private double _catLeft, _catTop, _catW, _catH;

    public ChatInputWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Deactivated += (_, _) => Close();   // click elsewhere => dismiss
    }

    /// <summary>Remember where the cat is so we can sit just below it once we know our size.</summary>
    public void PlaceNear(double catLeft, double catTop, double catW, double catH)
    {
        _catLeft = catLeft; _catTop = catTop; _catW = catW; _catH = catH;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Centre under the cat, then nudge fully on-screen.
        Left = _catLeft + _catW / 2 - ActualWidth / 2;
        Top  = _catTop + _catH + 6;

        var work = SystemParameters.WorkArea;
        Left = Math.Max(work.Left, Math.Min(Left, work.Right  - ActualWidth));
        Top  = Math.Max(work.Top,  Math.Min(Top,  work.Bottom - ActualHeight));

        Activate();
        Input.Focus();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string text = Input.Text.Trim();
            if (text.Length > 0) Submitted?.Invoke(text);
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
