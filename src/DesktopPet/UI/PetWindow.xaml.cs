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
using DesktopPet.Native;

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

    /// <summary>
    /// The engine's DIP scale: the PRIMARY monitor's, not this window's. With PerMonitorV2 the
    /// window's own DPI flips as it crosses onto a differently-scaled monitor, and dividing by
    /// that re-maps every coordinate mid-walk (the cat leapt from one screen's edge to the
    /// middle of the next). One fixed scale keeps the engine in a single coordinate space.
    /// </summary>
    public double DpiScale => Win32.PrimaryMonitorScale();

    // Last position the engine asked for, in engine units. WPF's Left/Top can't be trusted for
    // this: they're expressed in whichever DPI the window is currently on.
    private double _left = double.NaN, _top = double.NaN;
    private int _physX = int.MinValue, _physY = int.MinValue;

    public double PetLeft => _left;
    public double PetTop  => _top;

    /// <summary>
    /// Keep the cat the same physical size on every monitor. WPF enlarges content by each
    /// monitor's scale, so on a 125% screen the window would outgrow the size the engine plans
    /// around — feet sinking into the taskbar and the far edge hanging off-screen. Scaling the
    /// content by engineScale / windowScale cancels that out.
    /// </summary>
    private void SyncContentScale()
    {
        if (Content is not FrameworkElement root) return;
        double k = DpiScale / VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (root.LayoutTransform is ScaleTransform st && Math.Abs(st.ScaleX - k) < 1e-6) return;
        root.LayoutTransform = Math.Abs(k - 1) < 1e-6 ? Transform.Identity : new ScaleTransform(k, k);
    }

    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        SyncContentScale();
        // Windows just moved us to its own "suggested" rect for the new DPI; put the cat back
        // exactly where the engine has it.
        _physX = _physY = int.MinValue;
        if (!double.IsNaN(_left)) PlaceAt(_left, _top);
    }

    private bool _renderLoopRunning;

    public void Attach(PetEngine engine, double w, double h)
    {
        _engine = engine;
        SetPetSize(w, h);

        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        SetRenderLoopEnabled(true);
    }

    /// <summary>
    /// Start or stop the per-frame loop.
    ///
    /// Capping the tick rate inside the handler saves very little, because subscribing to
    /// CompositionTarget.Rendering makes WPF composite every frame whether or not we do any
    /// work in it. Detaching the handler is what actually lets the app go idle, so pausing
    /// really does stop the cost rather than just skipping the simulation.
    /// </summary>
    public void SetRenderLoopEnabled(bool enabled)
    {
        if (enabled == _renderLoopRunning) return;
        _renderLoopRunning = enabled;

        if (enabled)
        {
            // Don't let the paused interval arrive as one huge dt.
            _lastSeconds = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    public void SetPetSize(double w, double h)
    {
        PetImage.Width  = w;
        PetImage.Height = h;
        FlipTransform.CenterX = w / 2;
        HeatFlip.CenterX = w / 2;
    }

    /// <summary>
    /// Simulation tick budget, for very high refresh-rate displays where the engine would
    /// otherwise run several times more often than anything can show. 0 follows the compositor.
    ///
    /// This is NOT a power optimisation, despite appearances: skipping work inside the handler
    /// saves almost nothing, because the cost is being subscribed to Rendering at all. Use
    /// <see cref="SetRenderLoopEnabled"/> for that.
    /// </summary>
    public int TargetFps { get; set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        double now = _clock.Elapsed.TotalSeconds;

        int fps = TargetFps;
        if (fps > 0)
        {
            // Skip this compositor frame if the budget has not elapsed. dt then accumulates,
            // so the simulation still advances by real time rather than running slow.
            double minStep = 1.0 / fps;
            if (now - _lastSeconds < minStep) return;
        }

        double dt = Math.Min(now - _lastSeconds, 0.1);
        _lastSeconds = now;
        if (dt <= 0) return;
        _engine.Update(dt);
    }

    public void SetPetVisible(bool visible)
    {
        // Collapsing the image rather than hiding the window keeps the HWND alive, which the
        // global hotkey is registered against.
        PetImage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RedOverlay.Visibility = PetImage.Visibility;
        if (!visible)
        {
            Effects?.ClearSpeechBubble();
            Effects?.ClearToiletPaper();
        }
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
        PlaceAt(left, top);
    }

    private void PlaceAt(double left, double top)
    {
        _left = left;
        _top  = top;

        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero)
        {
            Left = left;
            Top  = top;
            return;
        }

        SyncContentScale();
        double s = DpiScale;
        int x = (int)Math.Round(left * s), y = (int)Math.Round(top * s);
        if (x == _physX && y == _physY) return;
        _physX = x;
        _physY = y;
        Win32.MoveWindowPhysical(hwnd, x, y);
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
        Effects?.Burst(_left + PetImage.Width / 2, _top);
    }

    public void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length)
        => Effects?.DrawToiletPaper(catLeft, catTop, catW, catH, length);

    public void ClearToiletPaper()
        => Effects?.ClearToiletPaper();

    public void DropPebble(double screenX, double screenY)
        => Effects?.DropPebble(screenX, screenY);

    public void DrawSpeechBubble(double catLeft, double catTop, double catW, double catH, string text)
        => Effects?.DrawSpeechBubble(catLeft, catTop, catW, catH, text);

    public void ClearSpeechBubble()
        => Effects?.ClearSpeechBubble();

    /// <summary>Raised when the user taps the cat and the AI companion is enabled — App opens the chat box.</summary>
    public event Action? ChatRequested;

    /// <summary>When true, a tap on the cat opens the AI chat box (in addition to the happy boop).</summary>
    public bool AiTapOpensChat { get; set; }

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
        {
            _engine?.Boop();            // tap with no movement => boop
            if (AiTapOpensChat) ChatRequested?.Invoke();
        }
        _pressing = false;
        _dragging = false;
        if (IsMouseOver)
            _engine?.BeginPet();
        e.Handled = true;
    }
}
