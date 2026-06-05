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
        PetImage.Width  = w;
        PetImage.Height = h;
        FlipTransform.CenterX = w / 2;

        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
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
        FlipTransform.ScaleX = facing >= 0 ? 1 : -1;
        Left = left;
        Top  = top;
    }

    public void SetHeatLevel(double level)
    {
        RedOverlay.Opacity = level * 0.55;
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _engine?.BeginDrag();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        _engine?.EndDrag();
        if (IsMouseOver)
            _engine?.BeginPet();
        e.Handled = true;
    }
}
