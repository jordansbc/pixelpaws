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

    // ── Toilet paper (free-standing holder beside the cat, paper hanging straight down) ──
    private readonly List<FrameworkElement> _tpParts = new();
    private System.Windows.Shapes.Rectangle? _tpPole;
    private Ellipse? _tpBase, _tpRoll, _tpHole, _tpPile;
    private Border?  _tpSheet;
    private static readonly SolidColorBrush PaperOutline = new(Color.FromRgb(180, 180, 198));
    private static readonly SolidColorBrush HolderBrush  = new(Color.FromRgb(170, 174, 190));

    private static System.Windows.Media.Effects.DropShadowEffect SoftShadow() =>
        new() { Color = Colors.Black, BlurRadius = 7, ShadowDepth = 1, Opacity = 0.22 };

    /// <summary>
    /// Draw/refresh a toilet-paper holder beside the cat: a little stand with a roll near the top
    /// and a perforated sheet hanging straight down, `length` DIP long (clamped to the floor).
    /// </summary>
    public void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length)
    {
        double rollR  = Math.Max(9, Math.Min(15, catW * 0.10));
        double sheetW = rollR * 1.25;

        if (_tpSheet == null)
        {
            // Horizontal perforation ticks tiled down the hanging sheet.
            var perf = new DrawingBrush
            {
                TileMode      = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport      = new Rect(0, 0, sheetW, 16),
                Drawing = new GeometryDrawing(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(55, 150, 150, 170)), 1.1)
                    { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) },
                    new LineGeometry(new Point(0, 15), new Point(sheetW, 15)))
            };
            _tpPole  = new System.Windows.Shapes.Rectangle { Fill = HolderBrush, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false, Effect = SoftShadow() };
            _tpBase  = new Ellipse { Fill = HolderBrush, IsHitTestVisible = false, Effect = SoftShadow() };
            _tpSheet = new Border
            {
                Background = Brushes.White, BorderBrush = PaperOutline, BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2, 2, 4, 4), IsHitTestVisible = false,
                Child = new System.Windows.Shapes.Rectangle { Fill = perf }, Effect = SoftShadow()
            };
            _tpRoll  = new Ellipse { Fill = Brushes.White, Stroke = PaperOutline, StrokeThickness = 1.5, IsHitTestVisible = false, Effect = SoftShadow() };
            _tpHole  = new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(150, 150, 168)), IsHitTestVisible = false };
            _tpPile  = new Ellipse { Fill = Brushes.White, Stroke = PaperOutline, StrokeThickness = 1.5, IsHitTestVisible = false, Effect = SoftShadow() };

            // z-order: pole/base behind, then sheet, then roll + hole on top.
            foreach (var e in new FrameworkElement[] { _tpPole, _tpBase, _tpPile, _tpSheet, _tpRoll, _tpHole })
            {
                _canvas.Children.Add(e);
                _tpParts.Add(e);
            }
        }

        // Local (overlay) coordinates.
        double catCx  = catLeft + catW / 2 - Left;
        double feetY  = catTop + catH - Top;

        // Holder stands right beside the cat (on the roomier side), with the roll up at about
        // the cat's reaching-paw height so the paper hangs down next to the paw.
        int side = catCx > Width / 2 ? -1 : 1;
        double rollCx = catCx + side * (catW * 0.34);
        double rollCy = feetY - catH * 0.50;

        // Pole + base (thin free-standing stand).
        double poleW = Math.Max(4, rollR * 0.28);
        _tpPole!.Width = poleW; _tpPole.Height = Math.Max(0, feetY - rollCy);
        Canvas.SetLeft(_tpPole, rollCx - poleW / 2); Canvas.SetTop(_tpPole, rollCy);
        _tpBase!.Width = rollR * 2.0; _tpBase.Height = rollR * 0.5;
        Canvas.SetLeft(_tpBase, rollCx - rollR); Canvas.SetTop(_tpBase, feetY - rollR * 0.35);

        // Hanging sheet: from just under the roll, down `length`, clamped to the floor.
        double sheetTop = rollCy + rollR * 0.5;
        double sheetLen = Math.Max(0, Math.Min(length, feetY - sheetTop - 2));
        _tpSheet!.Width = sheetW; _tpSheet.Height = sheetLen;
        Canvas.SetLeft(_tpSheet, rollCx - sheetW / 2); Canvas.SetTop(_tpSheet, sheetTop);

        // Roll + hole sit on top.
        _tpRoll!.Width = _tpRoll.Height = rollR * 2;
        Canvas.SetLeft(_tpRoll, rollCx - rollR); Canvas.SetTop(_tpRoll, rollCy - rollR);
        _tpHole!.Width = _tpHole.Height = rollR * 0.7;
        Canvas.SetLeft(_tpHole, rollCx - rollR * 0.35); Canvas.SetTop(_tpHole, rollCy - rollR * 0.35);

        // Small pile when the paper has reached the floor.
        bool piled = sheetLen >= feetY - sheetTop - 3 && length > 30;
        _tpPile!.Visibility = piled ? Visibility.Visible : Visibility.Collapsed;
        _tpPile.Width = sheetW + 14; _tpPile.Height = rollR * 0.9;
        Canvas.SetLeft(_tpPile, rollCx - (sheetW + 14) / 2); Canvas.SetTop(_tpPile, feetY - rollR * 0.7);
    }

    public void ClearToiletPaper()
    {
        foreach (var e in _tpParts) _canvas.Children.Remove(e);
        _tpParts.Clear();
        _tpPole = null; _tpBase = null; _tpRoll = null; _tpHole = null; _tpPile = null; _tpSheet = null;
    }

    /// <summary>A little pebble tumbles down from (x,y) with gravity and fades out.</summary>
    public void DropPebble(double screenX, double screenY)
    {
        var pebble = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(120, 116, 130)),
            Stroke = new SolidColorBrush(Color.FromRgb(80, 78, 92)), StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(pebble, screenX - Left - 6);
        Canvas.SetTop(pebble, screenY - Top - 6);
        _canvas.Children.Add(pebble);

        double vy = 0, x = screenX - Left - 6, y = screenY - Top - 6;
        double vx = (_rng.NextDouble() - 0.5) * 60;
        int tick = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            vy += 38;                       // gravity per tick
            y += vy * 0.016; x += vx * 0.016;
            Canvas.SetTop(pebble, y); Canvas.SetLeft(pebble, x);
            if (++tick > 55) { pebble.Opacity = Math.Max(0, pebble.Opacity - 0.08); }
            if (tick > 70) { timer.Stop(); _canvas.Children.Remove(pebble); }
        };
        timer.Start();
    }

    // ── Speech bubble (AI companion) — a rounded card floating above the cat's head ──
    private Border?    _bubble;
    private TextBlock? _bubbleInk;
    private string     _bubbleCurrent = "";
    private static readonly SolidColorBrush BubbleFill   = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush BubbleStroke = new(Color.FromRgb(190, 180, 214));
    private static readonly SolidColorBrush BubbleInk    = new(Color.FromRgb(58, 50, 72));

    /// <summary>Draw/refresh a speech bubble centred above the cat. Re-called every frame so it tracks the cat.</summary>
    public void DrawSpeechBubble(double catLeft, double catTop, double catW, double catH, string text)
    {
        if (_bubble == null)
        {
            _bubbleInk = new TextBlock
            {
                Foreground = BubbleInk, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 220, IsHitTestVisible = false
            };
            _bubble = new Border
            {
                Background = BubbleFill, BorderBrush = BubbleStroke, BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7),
                IsHitTestVisible = false, Effect = SoftShadow(), Child = _bubbleInk
            };
            _canvas.Children.Add(_bubble);
        }

        if (text != _bubbleCurrent)
        {
            _bubbleCurrent = text;
            _bubbleInk!.Text = text;
            // Fresh measure so DesiredSize is accurate for centring.
            _bubble.Measure(new System.Windows.Size(240, double.PositiveInfinity));
        }

        double bw = _bubble.DesiredSize.Width  > 0 ? _bubble.DesiredSize.Width  : 60;
        double bh = _bubble.DesiredSize.Height > 0 ? _bubble.DesiredSize.Height : 30;

        // Overlay-local coords (same conversion as DrawToiletPaper): centred above the cat's head.
        double catCx = catLeft + catW / 2 - Left;
        double headY = catTop - Top;
        double x = catCx - bw / 2;
        double y = headY - bh - 8;

        // Keep the whole bubble on-screen.
        x = Math.Max(2, Math.Min(x, Width  - bw - 2));
        y = Math.Max(2, Math.Min(y, Height - bh - 2));
        Canvas.SetLeft(_bubble, x);
        Canvas.SetTop(_bubble, y);
    }

    public void ClearSpeechBubble()
    {
        if (_bubble != null) { _canvas.Children.Remove(_bubble); _bubble = null; _bubbleInk = null; }
        _bubbleCurrent = "";
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
