using System.Windows;
using System.Windows.Media;
using DesktopPet.Native;
using DesktopPet.Services;
using Point = System.Windows.Point;

namespace DesktopPet.Engine;

/// <summary>The view the engine drives — implemented by PetWindow.</summary>
public interface IPetView
{
    double DpiScale { get; }
    void Render(ImageSource frame, double left, double top, int facing);
    /// <summary>Heat level 0–1 from typing speed. View applies a red tint.</summary>
    void SetHeatLevel(double level);
    /// <summary>Burst of floating hearts at the current cat position.</summary>
    void SpawnHearts();
    /// <summary>Draw/refresh a toilet-paper holder beside the cat with paper hanging down `length` (DIP).</summary>
    void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length);
    /// <summary>Remove the toilet-paper strip.</summary>
    void ClearToiletPaper();
}

/// <summary>
/// Per-frame simulation. All positions in DIPs.
/// New behaviours: cursor watching (idle), pet on hover, typing detection, stretch reminder.
/// </summary>
public sealed class PetEngine
{
    // ── tunables ──────────────────────────────────────────────────────────────
    private const double Gravity          = 2000;
    private const double MaxFall          = 2600;
    private const double BaseWalkSpeed    = 55;
    private const double BaseChaseSpeed   = 115;
    private const double SupportTolerance = 3.0;
    private const double SurfaceRefresh   = 0.25;
    private const double HeartInterval    = 0.45;  // seconds between heart bursts while petted
    private const double TypingCooldown   = 0.8;   // seconds after last key before leaving typing

    // ── injected ──────────────────────────────────────────────────────────────
    private readonly IPetView          _view;
    private readonly SpriteAnimator    _anim;
    private readonly SurfaceProvider   _surfaceProvider;
    private readonly StateMachine      _sm;
    private readonly AppSettings       _settings;
    private readonly KeyboardMonitor?  _keyboard;
    private readonly MouseMonitor?     _mouse;

    // ── physics / position ────────────────────────────────────────────────────
    private readonly double _w, _h;
    private double _x, _y;
    private double _vx, _vy;
    private int    _facing = 1;

    // ── state ─────────────────────────────────────────────────────────────────
    private PetState _state = PetState.Idle;
    private double   _stateTimer;
    private double   _surfaceTimer;
    private double   _heartTimer;
    private double   _typingCooldownLeft;
    private bool     _stretchPending;
    private bool     _isPetted;            // true while mouse hovers over the window
    private double   _tpLength;            // current toilet-paper length (DIP)
    private double   _tpIdle;              // seconds since last scroll
    private List<Surface> _surfaces = new();

    private const double TpPerNotch  = 26.0;   // paper added per scroll notch
    private const double TpMax       = 170.0;  // max paper length (hangs to about the floor)
    private const double TpRetract   = 200.0;  // px/sec retract speed once idle
    private const double TpIdleDelay = 1.2;    // seconds of no scroll before retracting

    public bool   Paused         { get; set; }
    public double Width          => _w;
    public double Height         => _h;

    // Let the App/scheduler set this to trigger a stretch at the next opportunity.
    public void RequestStretch() => _stretchPending = true;

    public PetEngine(IPetView view, SpriteAnimator anim, SurfaceProvider surfaceProvider,
                     StateMachine sm, AppSettings settings, KeyboardMonitor? keyboard = null,
                     MouseMonitor? mouse = null)
    {
        _view            = view;
        _anim            = anim;
        _surfaceProvider = surfaceProvider;
        _sm              = sm;
        _settings        = settings;
        _keyboard        = keyboard;
        _mouse           = mouse;

        _w = anim.Manifest.CellWidth  * anim.Manifest.Scale;
        _h = anim.Manifest.CellHeight * anim.Manifest.Scale;

        var work = SystemParameters.WorkArea;
        _x = work.Left + (work.Width - _w) / 2;
        SetFeet(work.Bottom);
        RefreshSurfaces();
        EnterState(PetState.Idle);
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private double CenterX      => _x + _w / 2;
    private double FeetY        => _y + _h;
    private void   SetFeet(double fy)   => _y = fy - _h;
    private void   SetCenterX(double cx) => _x = cx - _w / 2;
    private double Speed        => Math.Max(0.2, _settings.Speed);

    // ── external input ────────────────────────────────────────────────────────

    public void BeginDrag()
    {
        _isPetted = false;
        EnterState(PetState.Drag);
    }

    public void EndDrag()
    {
        _vx = 0; _vy = 0;
        EnterState(PetState.Fall);
    }

    public void BeginPet()
    {
        _isPetted = true;
        if (_state != PetState.Drag && _state != PetState.Fall)
        {
            _heartTimer = 0;  // spawn hearts immediately
            EnterState(PetState.Pet);
        }
    }

    public void EndPet()
    {
        _isPetted = false;
        if (_state == PetState.Pet)
            EnterState(PetState.Idle);
    }

    // ── main loop ─────────────────────────────────────────────────────────────

    public void Update(double dt)
    {
        if (Paused)
        {
            _view.Render(_anim.Tick(0), _x, _y, _facing);
            _view.SetHeatLevel(0);
            return;
        }

        // Surface refresh
        _surfaceTimer -= dt;
        if (_surfaceTimer <= 0) { RefreshSurfaces(); _surfaceTimer = SurfaceRefresh; }

        // ── scroll → toilet-paper play ──────────────────────────────────────────
        UpdateToiletPaper(dt);

        // ── typing detection (overrides normal states, but not drag/fall/pet/stretch/play) ──
        if (_state != PetState.Drag && _state != PetState.Fall &&
            _state != PetState.Pet  && _state != PetState.Stretch &&
            _state != PetState.Play)
        {
            float kps = _keyboard?.KeysPerSecond ?? 0;

            if (kps > 0.5f) _typingCooldownLeft = TypingCooldown;
            else            _typingCooldownLeft -= dt;

            bool inTypingWindow = _typingCooldownLeft > 0;

            if (inTypingWindow)
            {
                double heat = Math.Clamp((kps - 4) / 7.0, 0, 1);
                _view.SetHeatLevel(heat);

                PetState target = kps > 7.5f ? PetState.TypingFast : PetState.Typing;
                if (_state != target) EnterState(target);
            }
            else
            {
                _view.SetHeatLevel(0);
                if (_state == PetState.Typing || _state == PetState.TypingFast)
                    EnterState(PetState.Idle);
            }
        }

        // ── pet hover hearts ──────────────────────────────────────────────────
        if (_state == PetState.Pet)
        {
            _heartTimer -= dt;
            if (_heartTimer <= 0)
            {
                _view.SpawnHearts();
                _heartTimer = HeartInterval;
            }
        }

        // ── per-state update ──────────────────────────────────────────────────
        switch (_state)
        {
            case PetState.Idle:       UpdateIdle(dt);       break;
            case PetState.Walk:       UpdateWalk(dt);       break;
            case PetState.Chase:      UpdateChase(dt);      break;
            case PetState.Sleep:      UpdateStationary(dt); break;
            case PetState.Eat:        UpdateEat(dt);        break;
            case PetState.Pet:        UpdatePet(dt);        break;
            case PetState.Stretch:    UpdateStretch(dt);    break;
            case PetState.Typing:
            case PetState.TypingFast: UpdateTyping(dt);     break;
            case PetState.Play:       UpdateStationary(dt); break;
            case PetState.Fall:       UpdateFall(dt);       break;
            case PetState.Drag:       UpdateDrag(dt);       break;
        }

        _view.Render(_anim.Tick(dt), _x, _y, _facing);
    }

    // ── state updates ─────────────────────────────────────────────────────────

    private void UpdateIdle(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }

        // Always watch the cursor — face toward it even while idle
        var cursor = CursorDip();
        int dir = Math.Sign(cursor.X - CenterX);
        if (dir != 0) _facing = dir;

        _stateTimer -= dt;
        if (_stateTimer <= 0)
        {
            if (_stretchPending) { _stretchPending = false; EnterState(PetState.Stretch); return; }
            EnterState(_sm.NextAction());
        }
    }

    private void UpdateWalk(double dt)
    {
        _x += _vx * dt * Speed;
        if (ClampHorizontally()) FlipDirection();

        if (!IsSupported())
        {
            _vx = _facing * BaseWalkSpeed * Speed * 0.5;
            EnterState(PetState.Fall);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Idle);
    }

    private void UpdateChase(double dt)
    {
        double cursorX = CursorDip().X;
        int dir = Math.Sign(cursorX - CenterX);
        if (dir != 0) _facing = dir;
        _x += dir * BaseChaseSpeed * Speed * dt;
        if (ClampHorizontally()) { /* keep trying */ }

        if (!IsSupported())
        {
            _vx = dir * BaseChaseSpeed * Speed * 0.5;
            EnterState(PetState.Fall);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0 || Math.Abs(cursorX - CenterX) < 10)
            EnterState(PetState.Idle);
    }

    private void UpdateStationary(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Idle);
    }

    private void UpdateEat(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        if (_anim.Finished) EnterState(PetState.Idle);
    }

    private void UpdatePet(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        // Face the cursor lovingly
        var cursor = CursorDip();
        int dir = Math.Sign(cursor.X - CenterX);
        if (dir != 0) _facing = dir;
        // State exit handled by EndPet()
    }

    private void UpdateStretch(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        if (_anim.Finished) EnterState(PetState.Idle);
    }

    private void UpdateTyping(double dt)
    {
        // Stationary — typing detection in the main Update loop handles transitions
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
    }

    /// <summary>Scroll wheel makes the cat unroll toilet paper; it retracts once you stop.</summary>
    private void UpdateToiletPaper(double dt)
    {
        // Don't play while being dragged or mid-air.
        bool canPlay = _settings.EnableScrollPlay &&
                       _state != PetState.Drag && _state != PetState.Fall;

        double notches = _mouse?.TakeScrollNotches() ?? 0;
        if (notches > 0 && canPlay)
        {
            _tpLength = Math.Min(_tpLength + notches * TpPerNotch, TpMax);
            _tpIdle = 0;
            if (_state != PetState.Play) EnterState(PetState.Play);
        }

        if (_tpLength > 0)
        {
            _tpIdle += dt;
            if (_tpIdle > TpIdleDelay)
                _tpLength = Math.Max(0, _tpLength - TpRetract * dt);

            // Holder stands beside the cat; if support is lost (dragged), drop it.
            if (canPlay)
                _view.DrawToiletPaper(_x, _y, _w, _h, _tpLength);
            else
                _tpLength = 0;

            if (_tpLength <= 0)
            {
                _view.ClearToiletPaper();
                if (_state == PetState.Play) EnterState(PetState.Idle);
            }
        }
    }

    private void UpdateFall(double dt)
    {
        _vy = Math.Min(_vy + Gravity * dt, MaxFall);
        double prevFeet = FeetY;
        double nextFeet = prevFeet + _vy * dt;

        _x += _vx * dt;
        ClampHorizontally();

        if (TryLand(prevFeet, nextFeet, out double landTop))
        {
            SetFeet(landTop);
            _vx = 0; _vy = 0;
            EnterState(_isPetted ? PetState.Pet : PetState.Idle);
        }
        else
        {
            SetFeet(nextFeet);
        }
    }

    private void UpdateDrag(double dt)
    {
        var c = CursorDip();
        SetCenterX(c.X);
        _y = c.Y - _h / 2;
        ClampHorizontally();
    }

    // ── state entry ───────────────────────────────────────────────────────────

    private void EnterState(PetState state)
    {
        _state = state;
        switch (state)
        {
            case PetState.Idle:
                _vx = 0; _vy = 0;
                _stateTimer = _sm.NextIdleDuration();
                _anim.Play("idle");
                break;
            case PetState.Walk:
                _facing = _sm.RandomFacing();
                _vx = _facing * BaseWalkSpeed;
                _vy = 0;
                _stateTimer = _sm.WalkDuration();
                _anim.Play("walk");
                break;
            case PetState.Chase:
                _vy = 0;
                _stateTimer = _sm.ChaseDuration();
                _anim.Play("walk");
                break;
            case PetState.Sleep:
                _vx = 0; _vy = 0;
                _stateTimer = _sm.SleepDuration();
                _anim.Play("sleep");
                break;
            case PetState.Eat:
                _vx = 0; _vy = 0;
                _anim.Play("eat");
                break;
            case PetState.Pet:
                _vx = 0; _vy = 0;
                _heartTimer = 0;
                _anim.Play("pet");
                break;
            case PetState.Stretch:
                _vx = 0; _vy = 0;
                _anim.Play("stretch");
                break;
            case PetState.Typing:
                _vx = 0; _vy = 0;
                _anim.Play("typing");
                break;
            case PetState.TypingFast:
                _vx = 0; _vy = 0;
                _anim.Play("typingfast");
                break;
            case PetState.Play:
                _vx = 0; _vy = 0;
                _anim.Play("pet");   // happy/excited pose while pawing at the paper
                break;
            case PetState.Fall:
                _anim.Play("fall");
                break;
            case PetState.Drag:
                _vx = 0; _vy = 0;
                _anim.Play("drag");
                break;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void RefreshSurfaces()
    {
        if (_settings.EnableWindowWalking)
            _surfaces = _surfaceProvider.GetSurfaces(_view.DpiScale);
        else
        {
            var work = SystemParameters.WorkArea;
            _surfaces = new List<Surface> { new(work.Left, work.Right, work.Bottom) };
        }
    }

    private bool IsSupported()
    {
        double cx = CenterX, fy = FeetY;
        foreach (var s in _surfaces)
            if (s.ContainsX(cx) && Math.Abs(s.Top - fy) <= SupportTolerance)
                return true;
        return false;
    }

    private bool TryLand(double prevFeet, double nextFeet, out double landTop)
    {
        landTop = 0;
        bool found = false;
        double cx = CenterX;
        foreach (var s in _surfaces)
        {
            if (!s.ContainsX(cx)) continue;
            if (s.Top < prevFeet - SupportTolerance) continue;
            if (s.Top > nextFeet) continue;
            if (!found || s.Top < landTop) { landTop = s.Top; found = true; }
        }
        return found;
    }

    private bool ClampHorizontally()
    {
        var work = SystemParameters.WorkArea;
        if (_x < work.Left)      { _x = work.Left;          return true; }
        if (_x + _w > work.Right){ _x = work.Right - _w;    return true; }
        return false;
    }

    private void FlipDirection() { _facing = -_facing; _vx = -_vx; }

    private Point CursorDip()
    {
        Win32.GetCursorPos(out var p);
        double s = _view.DpiScale;
        return new Point(p.X / s, p.Y / s);
    }
}
