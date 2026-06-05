using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Pen = System.Windows.Media.Pen;

namespace DesktopPet.UI;

/// <summary>
/// A single persistent, transparent, click-through, top-most window covering the whole
/// virtual screen. Hearts/sparkles are drawn as Canvas children here instead of as
/// per-effect windows — creating Windows inside the render loop throws and is expensive.
/// </summary>
public sealed class EffectsOverlay : Window
{
    private readonly Canvas _canvas = new();
    private readonly Random _rng = new();

    public EffectsOverlay()
    {
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        Topmost            = true;
        ShowInTaskbar      = false;
        ResizeMode         = ResizeMode.NoResize;
        IsHitTestVisible   = false;
        Content            = _canvas;

        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Whole-screen overlay must never intercept clicks meant for the cat or other apps.
        DesktopPet.Native.Win32.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    // ── Toilet paper ────────────────────────────────────────────────────────────
    private Border? _tp;       // the trailing strip along the surface
    private Ellipse? _tpPile;  // crumpled wad at the end

    /// <summary>
    /// Draw/refresh a toilet-paper trail of the given length (DIP) strewn along the surface the
    /// cat stands on, extending toward the roomier side of the screen so it's always visible.
    /// </summary>
    public void DrawToiletPaper(double screenX, double screenFeetY, double length)
    {
        const double paperWidth = 24; // visual thickness of the strip lying on the floor

        if (_tp == null)
        {
            // Vertical perforation ticks tiled along the strip so it reads as paper and is
            // visible on light backgrounds.
            var perf = new DrawingBrush
            {
                TileMode      = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport      = new Rect(0, 0, 26, paperWidth),
                Drawing = new GeometryDrawing(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(60, 150, 150, 170)), 1.2)
                    { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) },
                    new LineGeometry(new Point(25, 0), new Point(25, paperWidth)))
            };
            var outline = new SolidColorBrush(Color.FromRgb(176, 176, 196)); // visible on white
            _tp = new Border
            {
                Height           = paperWidth,
                Background       = Brushes.White,
                BorderBrush      = outline,
                BorderThickness  = new Thickness(1.5),
                CornerRadius     = new CornerRadius(3),
                IsHitTestVisible = false,
                // Perforation ticks drawn on top of the white fill.
                Child            = new System.Windows.Shapes.Rectangle { Fill = perf },
                Effect           = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1, Opacity = 0.25
                }
            };
            _tpPile = new Ellipse
            {
                Width = 32, Height = paperWidth + 12,
                Fill = Brushes.White,
                Stroke = outline, StrokeThickness = 1.5,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.25
                }
            };
            _canvas.Children.Add(_tp);
            _canvas.Children.Add(_tpPile);
        }

        double localX = screenX - Left;
        double localY = (screenFeetY - Top) - paperWidth; // sit the strip on the surface

        // Extend toward whichever side has more room so the trail stays on-screen.
        bool toLeft = localX > Width / 2;
        double stripLeft = toLeft ? localX - length : localX;

        _tp!.Width = length;
        Canvas.SetLeft(_tp, stripLeft);
        Canvas.SetTop(_tp, localY);

        double pileX = toLeft ? stripLeft - 12 : stripLeft + length - 18;
        _tpPile!.Visibility = length > 45 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_tpPile, pileX);
        Canvas.SetTop(_tpPile, localY - 5);
    }

    public void ClearToiletPaper()
    {
        if (_tp != null)      { _canvas.Children.Remove(_tp);     _tp = null; }
        if (_tpPile != null)  { _canvas.Children.Remove(_tpPile); _tpPile = null; }
    }

    /// <summary>Emit a small burst of hearts/sparkles. Coordinates are screen DIPs.</summary>
    public void Burst(double screenCenterX, double screenTopY)
    {
        int count = 2 + _rng.Next(2);
        for (int i = 0; i < count; i++)
            SpawnOne(screenCenterX, screenTopY);
    }

    private void SpawnOne(double sx, double syTop)
    {
        string[] symbols = { "♥", "♥", "♡", "✦", "✨" };
        byte r = (byte)(220 + _rng.Next(35));
        byte g = (byte)_rng.Next(60, 140);
        byte b = (byte)_rng.Next(120, 200);

        var tb = new TextBlock
        {
            Text             = symbols[_rng.Next(symbols.Length)],
            FontSize         = 18 + _rng.Next(12),
            Foreground       = new SolidColorBrush(Color.FromRgb(r, g, b)),
            IsHitTestVisible = false
        };

        // Convert screen DIPs to canvas (overlay-local) coordinates.
        double x = sx - Left + (_rng.NextDouble() - 0.5) * 40;
        double y = syTop - Top;
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        _canvas.Children.Add(tb);

        double driftX = (_rng.NextDouble() - 0.5) * 1.5;
        int tick = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        timer.Tick += (_, _) =>
        {
            Canvas.SetTop(tb, Canvas.GetTop(tb) - 2.8);
            Canvas.SetLeft(tb, Canvas.GetLeft(tb) + driftX);
            tb.Opacity = Math.Max(0, 1.0 - tick / 22.0);
            if (++tick >= 22)
            {
                timer.Stop();
                _canvas.Children.Remove(tb);
            }
        };
        timer.Start();
    }
}
