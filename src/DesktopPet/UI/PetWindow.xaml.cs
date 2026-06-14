using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Engine;

namespace DesktopPet.UI;

public partial class PetWindow : Window, IPetView
{
    private PetEngine? _engine;
    private readonly Stopwatch _clock = new();
    private double _lastSeconds;
    private readonly Random _rng = new();

    public PetWindow()
    {
        InitializeComponent();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public double DpiScale
    {
        get
        {
            var src = PresentationSource.FromVisual(this);
            return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    public void Attach(PetEngine engine, double w, double h)
    {
        _engine = engine;
        SetPetSize(w, h);

        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    public void SetPetSize(double w, double h)
    {
        PetImage.Width  = w;
        PetImage.Height = h;
        FlipTransform.CenterX = w / 2;
        HeatFlip.CenterX = w / 2;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        double now = _clock.Elapsed.TotalSeconds;
        double dt  = Math.Min(now - _lastSeconds, 0.1);
        _lastSeconds = now;
        if (dt <= 0) return;
        _engine.Update(dt);
    }

    // ── IPetView ──────────────────────────────────────────────────────────────

    public void Render(ImageSource frame, double left, double top, int facing)
    {
        PetImage.Source = frame;
        double sx = facing >= 0 ? 1 : -1;
        FlipTransform.ScaleX = sx;
        // Keep the heat overlay aligned with the current frame and facing.
        HeatMaskBrush.ImageSource = frame;
        HeatFlip.ScaleX = sx;
        Left = left;
        Top  = top;
    }

    public void SetHeatLevel(double level)
    {
        // Red "hot" flush, clipped to the cat silhouette by the OpacityMask. Capped well
        // below full so the cat's features stay readable through the tint.
        RedOverlay.Opacity = Math.Clamp(level, 0, 1) * 0.55;
    }

    /// <summary>Persistent overlay that renders heart/sparkle particles. Set by App.</summary>
    public EffectsOverlay? Effects { get; set; }

    public void SpawnHearts()
    {
        // Hearts float up from just above the cat's head. Drawn on the shared overlay —
        // NOT as new windows (creating windows in the render loop throws + is costly).
        Effects?.Burst(Left + Width / 2, Top);
    }

    public void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length)
        => Effects?.DrawToiletPaper(catLeft, catTop, catW, catH, length);

    public void ClearToiletPaper()
        => Effects?.ClearToiletPaper();

    public void DropPebble(double screenX, double screenY)
        => Effects?.DropPebble(screenX, screenY);

    // ── Mouse events ──────────────────────────────────────────────────────────

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _engine?.BeginPet();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
            _engine?.EndPet();
    }

    private System.Windows.Point _downPos;
    private bool _pressing;
    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Don't start a drag yet — wait to see if this is a tap (boop) or a drag.
        _downPos  = PointToScreen(e.GetPosition(this));
        _pressing = true;
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressing && !_dragging)
        {
            var p = PointToScreen(e.GetPosition(this));
            if (Math.Abs(p.X - _downPos.X) + Math.Abs(p.Y - _downPos.Y) > 8)
            {
                _dragging = true;
                _engine?.BeginDrag();   // movement => pick it up
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        if (_dragging)
            _engine?.EndDrag();
        else
            _engine?.Boop();            // tap with no movement => boop
        _pressing = false;
        _dragging = false;
        if (IsMouseOver)
            _engine?.BeginPet();
        e.Handled = true;
    }
}
