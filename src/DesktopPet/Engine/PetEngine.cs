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
    /// <summary>Resize the pet window/image to the given DIP size.</summary>
    void SetPetSize(double w, double h);
    /// <summary>Heat level 0–1 from typing speed. View applies a red tint.</summary>
    void SetHeatLevel(double level);
    /// <summary>Burst of floating hearts at the current cat position.</summary>
    void SpawnHearts();
    /// <summary>Draw/refresh a toilet-paper holder beside the cat with paper hanging down `length` (DIP).</summary>
    void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length);
    /// <summary>Remove the toilet-paper strip.</summary>
    void ClearToiletPaper();
    /// <summary>A small pebble tumbles down from (screenX, screenY) and fades — cat knocking things off a ledge.</summary>
    void DropPebble(double screenX, double screenY);
    /// <summary>Draw/refresh a speech bubble above the cat with the given text (AI companion).</summary>
    void DrawSpeechBubble(double catLeft, double catTop, double catW, double catH, string text);
    /// <summary>Remove the speech bubble.</summary>
    void ClearSpeechBubble();
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
    private const double JumpSpeed        = 800;   // initial upward velocity for a hop (~160px high, ~0.80s airtime)
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
    private readonly SystemMonitor?    _system;

    // ── physics / position ────────────────────────────────────────────────────
    private double _w, _h;                 // current on-screen pet size (DIP); changes with SizeScale
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
    private bool     _wasAway;             // user was idle long enough that the cat napped
    private double   _spinFlip;            // countdown to next facing flip during a spin
    private double   _tpLength;            // current toilet-paper length (DIP)
    private double   _tpIdle;              // seconds since last scroll
    private double   _lastDragX, _lastDragY; // last cursor pos while dragging (DIP)
    private double   _prevCurX, _prevCurY; // cursor position last frame (DIP)
    private double   _cursorSpeed;         // cursor speed (DIP/sec)
    private double   _batCooldown;         // seconds until the cat may swat again
    private double   _huntCooldown;        // seconds until the cat may hunt again
    private double   _knockCooldown;       // seconds until the cat may knock a pebble again
    private double   _targetX;             // generic target (pounce/gift) in DIP

    // ── AI companion ──────────────────────────────────────────────────────────
    private PetState? _pendingEmotion;     // emotion queued while the cat is mid-action
    private string    _bubbleText = "";    // current speech-bubble text ("" = none)
    private double    _bubbleTimeLeft;     // seconds the bubble stays up

    private const double ZoomSpeed   = 360;  // px/sec during zoomies
    private const double BatRange    = 150;  // how close the cursor must rest to be swatted
    private const double BatCalm     = 140;  // cursor must be slower than this to be swatted
    private const double HuntTrigger = 1500; // cursor speed (px/sec) that starts a hunt
    private const double CreepSpeed  = 75;   // px/sec while stalking
    private const double PounceSpeed = 540;  // px/sec while pouncing
    private const double PounceRange = 360;  // max horizontal distance to pounce from
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

    /// <summary>AI companion: play an emotion animation. Applied at once unless the cat is
    /// mid-action (drag/fall/pounce/hunt/jump), in which case it's queued for the next settle.</summary>
    public void RequestEmotion(PetState state)
    {
        if (_state is PetState.Drag or PetState.Fall or PetState.Pounce or PetState.Hunt or PetState.Jump)
        {
            _pendingEmotion = state;
            return;
        }
        _pendingEmotion = null;
        EnterState(state);
    }

    /// <summary>AI companion: show a speech bubble above the cat for `seconds`. It tracks the cat each frame.</summary>
    public void ShowSpeech(string text, double seconds)
    {
        _bubbleText     = text ?? "";
        _bubbleTimeLeft = Math.Max(0, seconds);
    }

    /// <summary>Hide the speech bubble immediately.</summary>
    public void ClearSpeech()
    {
        _bubbleTimeLeft = 0;
        _bubbleText     = "";
        _view.ClearSpeechBubble();
    }

    public PetEngine(IPetView view, SpriteAnimator anim, SurfaceProvider surfaceProvider,
                     StateMachine sm, AppSettings settings, KeyboardMonitor? keyboard = null,
                     MouseMonitor? mouse = null, SystemMonitor? system = null)
    {
        _view            = view;
        _anim            = anim;
        _surfaceProvider = surfaceProvider;
        _sm              = sm;
        _settings        = settings;
        _keyboard        = keyboard;
        _mouse           = mouse;
        _system          = system;

        _sizeScale = Math.Clamp(settings.SizeScale, 0.5, 2.0);
        _w = anim.Manifest.CellWidth  * anim.Manifest.Scale * _sizeScale;
        _h = anim.Manifest.CellHeight * anim.Manifest.Scale * _sizeScale;

        var work = SystemParameters.WorkArea;
        _x = work.Left + (work.Width - _w) / 2;
        SetFeet(work.Bottom);
        RefreshSurfaces();
        EnterState(PetState.Idle);
    }

    private double _sizeScale = 1.0;

    /// <summary>Change the cat's overall size at runtime, keeping its feet planted in place.</summary>
    public void ApplySize(double sizeScale)
    {
        sizeScale = Math.Clamp(sizeScale, 0.5, 2.0);
        if (Math.Abs(sizeScale - _sizeScale) < 0.001) return;

        double feet = FeetY, cx = CenterX;
        _sizeScale = sizeScale;
        _w = _anim.Manifest.CellWidth  * _anim.Manifest.Scale * _sizeScale;
        _h = _anim.Manifest.CellHeight * _anim.Manifest.Scale * _sizeScale;
        SetCenterX(cx);
        SetFeet(feet);
        _view.SetPetSize(_w, _h);
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
        // Drop from roughly where the cursor released so the compact cat doesn't jump.
        if (_lastDragY > 0) SetFeet(_lastDragY + _h * 0.30);
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

    /// <summary>A quick tap on the cat — happy boop with a heart burst.</summary>
    public void Boop()
    {
        if (_state == PetState.Drag || _state == PetState.Fall) return;
        _view.SpawnHearts();
        _view.SpawnHearts();
        _heartTimer = 0;
        EnterState(PetState.Pet);   // brief happy face; reverts on mouse-leave
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

        // Evolve mood drives; react to the machine (naps when you're away, greets your return).
        _sm.Tick(dt, _state);
        if (_settings.EnableSystemReactions && _system != null)
        {
            _system.Poll();
            UpdateAwayState();
        }

        // Track cursor speed (used by bat-at-cursor and, later, hunting).
        var cur = CursorDip();
        double cdx = cur.X - _prevCurX, cdy = cur.Y - _prevCurY;
        _cursorSpeed = Math.Sqrt(cdx * cdx + cdy * cdy) / Math.Max(dt, 1e-3);
        _prevCurX = cur.X; _prevCurY = cur.Y;
        if (_batCooldown > 0)   _batCooldown   -= dt;
        if (_huntCooldown > 0)  _huntCooldown  -= dt;
        if (_knockCooldown > 0) _knockCooldown -= dt;

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
            case PetState.Sleep:      UpdateSleep(dt);      break;
            case PetState.Eat:        UpdateEat(dt);        break;
            case PetState.Pet:        UpdatePet(dt);        break;
            case PetState.Stretch:    UpdateStretch(dt);    break;
            case PetState.Typing:
            case PetState.TypingFast: UpdateTyping(dt);     break;
            case PetState.Play:       UpdateStationary(dt); break;
            case PetState.Zoomies:    UpdateZoomies(dt);    break;
            case PetState.Bat:        UpdateBat(dt);        break;
            case PetState.Hunt:       UpdateHunt(dt);       break;
            case PetState.Pounce:     UpdatePounce(dt);     break;
            case PetState.Proud:      UpdateProud(dt);      break;
            case PetState.Groom:      UpdateStationary(dt); break;
            case PetState.Yawn:       UpdateYawn(dt);       break;
            case PetState.Loaf:       UpdateStationary(dt); break;
            case PetState.Gift:       UpdateGift(dt);       break;
            case PetState.Knockoff:   UpdateStationary(dt); break;
            case PetState.SideRest:   UpdateStationary(dt); break;
            case PetState.Wakeup:     UpdateWakeup(dt);     break;
            case PetState.Spin:       UpdateSpin(dt);       break;
            case PetState.Jump:       UpdateJump(dt);       break;
            case PetState.Talk:       UpdateStationary(dt); break;
            case PetState.Fall:       UpdateFall(dt);       break;
            case PetState.Drag:       UpdateDrag(dt);       break;
        }

        // ── speech bubble (AI companion) — redraw each frame so it follows the cat ──
        if (_bubbleTimeLeft > 0)
        {
            _bubbleTimeLeft -= dt;
            if (_bubbleTimeLeft > 0) _view.DrawSpeechBubble(_x, _y, _w, _h, _bubbleText);
            else                     { _bubbleText = ""; _view.ClearSpeechBubble(); }
        }

        _view.Render(_anim.Tick(dt), _x, _y, _facing);
    }

    // ── state updates ─────────────────────────────────────────────────────────

    private void UpdateIdle(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }

        // AI companion: a queued emotion takes priority the moment the cat is idle again.
        if (_pendingEmotion is { } emo) { _pendingEmotion = null; EnterState(emo); return; }

        if (TryStartHunt()) return;

        // Always watch the cursor — face toward it even while idle
        var cursor = CursorDip();
        int dir = Math.Sign(cursor.X - CenterX);
        if (dir != 0) _facing = dir;

        // Swat at the cursor when it rests still right beside the cat.
        if (_batCooldown <= 0 && _cursorSpeed < BatCalm)
        {
            double dx = Math.Abs(cursor.X - CenterX), dy = Math.Abs(cursor.Y - (FeetY - _h * 0.25));
            if (dx > _w * 0.30 && dx < BatRange && dy < BatRange)
            {
                EnterState(PetState.Bat);
                return;
            }
        }

        // On a window ledge, frequently paw a pebble off the edge.
        if (_knockCooldown <= 0 && OnLedge() && _sm.Chance(0.020))
        {
            EnterState(PetState.Knockoff);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0)
        {
            if (_stretchPending) { _stretchPending = false; EnterState(PetState.Stretch); return; }
            EnterState(_sm.NextAction());
        }
    }

    private const double AwaySeconds = 90.0;  // user idle this long => the cat curls up

    /// <summary>Nap while the user is away; give a happy greeting when they return.</summary>
    private void UpdateAwayState()
    {
        double idle = _system!.IdleSeconds;

        if (idle > AwaySeconds)
        {
            _wasAway = true;
            if (_settings.EnableSleep && IsSupported() &&
                _state is PetState.Idle or PetState.Walk or PetState.Loaf)
                EnterState(PetState.Sleep);
        }
        else if (_wasAway && idle < 2.0)
        {
            _wasAway = false;
            if (IsSupported() && _state is PetState.Sleep or PetState.Loaf or PetState.Idle)
                EnterState(PetState.Proud);   // "you're back!" — happy sparkle
        }
    }

    /// <summary>Begin stalking when the cursor whips past quickly.</summary>
    private bool TryStartHunt()
    {
        if (_huntCooldown <= 0 && _cursorSpeed > HuntTrigger && IsSupported())
        {
            EnterState(PetState.Hunt);
            return true;
        }
        return false;
    }

    /// <summary>True when the cat is standing on a raised window edge (not the desktop floor).</summary>
    private bool OnLedge()
    {
        return FeetY < SystemParameters.WorkArea.Bottom - 24;
    }

    private void UpdateWalk(double dt)
    {
        if (TryStartHunt()) return;

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

    /// <summary>Sleep, then wake with a little stretch instead of snapping awake.</summary>
    private void UpdateSleep(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Wakeup);
    }

    private void UpdateWakeup(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        if (_anim.Finished) EnterState(PetState.Idle);
    }

    /// <summary>Playful in-place spin — flip facing a few times, then settle.</summary>
    private void UpdateSpin(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        _spinFlip -= dt;
        if (_spinFlip <= 0) { _facing = -_facing; _spinFlip = 0.14; }
        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Idle);
    }

    private void UpdateZoomies(double dt)
    {
        _x += _vx * dt;
        if (ClampHorizontally()) { _facing = -_facing; _vx = -_vx; }   // bounce off screen edges

        if (!IsSupported())
        {
            _vx = _facing * ZoomSpeed * 0.4;
            EnterState(PetState.Fall);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Idle);
    }

    private void UpdateBat(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        // Keep facing the cursor while swatting.
        var cursor = CursorDip();
        int dir = Math.Sign(cursor.X - CenterX);
        if (dir != 0) _facing = dir;

        _stateTimer -= dt;
        // Stop early if the cursor darts away.
        if (_stateTimer <= 0 || Math.Abs(cursor.X - CenterX) > BatRange * 1.6)
            EnterState(PetState.Idle);
    }

    private void UpdateHunt(double dt)
    {
        if (!IsSupported()) { _huntCooldown = 4; EnterState(PetState.Fall); return; }

        var cur = CursorDip();
        int dir = Math.Sign(cur.X - CenterX);
        if (dir != 0) _facing = dir;
        double dx = Math.Abs(cur.X - CenterX);

        // Creep low toward the cursor.
        if (dx > 24) _x += dir * CreepSpeed * dt;
        ClampHorizontally();

        // Stalk for at least ~0.6s, then pounce once the cursor holds still within range.
        if (_stateTimer < 5.4 && _cursorSpeed < 220 && dx > 26 && dx < PounceRange)
        {
            _targetX = cur.X;
            EnterState(PetState.Pounce);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0) { _huntCooldown = 5; EnterState(PetState.Idle); }
    }

    private void UpdatePounce(double dt)
    {
        if (!IsSupported()) { _huntCooldown = 4; EnterState(PetState.Fall); return; }

        _x += _facing * PounceSpeed * dt;
        bool hitWall = ClampHorizontally();
        double dx = _targetX - CenterX;

        _stateTimer -= dt;
        if (hitWall || Math.Abs(dx) < 28 || Math.Sign(dx) != _facing || _stateTimer <= 0)
            EnterState(PetState.Proud);
    }

    private void UpdateProud(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        _stateTimer -= dt;
        if (_stateTimer <= 0) EnterState(PetState.Idle);
    }

    private void UpdateYawn(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        if (_anim.Finished) EnterState(PetState.Sleep);
    }

    private void UpdateGift(double dt)
    {
        if (!IsSupported()) { EnterState(PetState.Fall); return; }
        int dir = Math.Sign(_targetX - CenterX);
        if (dir != 0) _facing = dir;
        _x += dir * BaseWalkSpeed * 1.3 * dt;
        ClampHorizontally();

        _stateTimer -= dt;
        if (Math.Abs(_targetX - CenterX) < 26 || _stateTimer <= 0)
            EnterState(PetState.Proud);   // present it proudly
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

    private void UpdateJump(double dt)
    {
        _vy = Math.Min(_vy + Gravity * dt, MaxFall);   // gravity decelerates the rise, then pulls down
        double prevFeet = FeetY;
        double nextFeet = prevFeet + _vy * dt;

        _x += _vx * dt;
        if (ClampHorizontally()) _vx = -_vx;           // bounce off screen edges mid-hop

        // Only look for a landing once we're descending, so we don't "land" on lift-off.
        if (_vy > 0 && TryLand(prevFeet, nextFeet, out double landTop))
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
        _lastDragX = c.X; _lastDragY = c.Y;
        SetCenterX(c.X);
        // Lift frames are top-anchored (held by the scruff); keep the head near the cursor
        // so the cat dangles/stretches downward from it.
        _y = c.Y - _h * 0.06;
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
                // Face the roomier side, where the toilet-paper holder appears, so the
                // reaching paw points toward the paper.
                _facing = CenterX > System.Windows.SystemParameters.PrimaryScreenWidth / 2 ? -1 : 1;
                _anim.Play("play");  // sideways paw-reach toward the paper
                break;
            case PetState.Zoomies:
                _facing = _sm.RandomFacing();
                _vx = _facing * ZoomSpeed;
                _vy = 0;
                _stateTimer = _sm.ZoomiesDuration();
                _anim.Play("walk");
                break;
            case PetState.Bat:
                _vx = 0; _vy = 0;
                _batCooldown = 4.0;
                _stateTimer = 0.9;
                _anim.Play("play");   // reuse the sideways paw-reach as a swat
                break;
            case PetState.Hunt:
                _vx = 0; _vy = 0;
                _stateTimer = 6.0;    // give up stalking after a while
                _anim.Play("crouch");
                break;
            case PetState.Pounce:
                _vy = 0;
                _stateTimer = 0.9;
                _facing = Math.Sign(_targetX - CenterX) >= 0 ? 1 : -1;
                _anim.Play("pounce");
                break;
            case PetState.Proud:
                _vx = 0; _vy = 0;
                _stateTimer = 1.1;
                _huntCooldown = 5.0;
                _view.SpawnHearts();  // little sparkle of triumph
                _anim.Play("proud");
                break;
            case PetState.Groom:
                _vx = 0; _vy = 0;
                _stateTimer = _sm.GroomDuration();
                _anim.Play("groom");
                break;
            case PetState.Yawn:
                _vx = 0; _vy = 0;
                _anim.Play("yawn");   // non-looping; leads into sleep
                break;
            case PetState.Loaf:
                _vx = 0; _vy = 0;
                _stateTimer = _sm.LoafDuration();
                _anim.Play("loaf");
                break;
            case PetState.Gift:
                _vy = 0;
                _targetX = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2;
                _stateTimer = 7.0;
                _anim.Play("gift");
                break;
            case PetState.Knockoff:
                _vx = 0; _vy = 0;
                _stateTimer = 1.6;
                _knockCooldown = 6.0;
                // A pebble tumbles off the ledge in front of the cat.
                _view.DropPebble(_facing >= 0 ? _x + _w * 0.72 : _x + _w * 0.28, FeetY - _h * 0.05);
                _anim.Play("knockoff");
                break;
            case PetState.SideRest:
                _vx = 0; _vy = 0;
                _stateTimer = _sm.SideRestDuration();
                _anim.Play("siderest");
                break;
            case PetState.Wakeup:
                _vx = 0; _vy = 0;
                _anim.Play("wakeup");   // non-looping; reverts to idle when finished
                break;
            case PetState.Spin:
                _vx = 0; _vy = 0;
                _spinFlip = 0.14;
                _stateTimer = _sm.SpinDuration();
                _anim.Play("walk");     // legs churn while we flip facing -> looks like a spin
                break;
            case PetState.Jump:
                // Face the cursor and hop gently toward it; the existing fall physics handle the arc.
                _facing = Math.Sign(CursorDip().X - CenterX) is var d && d != 0 ? d : _facing;
                _vy = -JumpSpeed;
                _vx = _facing * BaseWalkSpeed * 0.6;
                _anim.Play("pounce");   // reuse the existing airborne/leaping frames — no new art
                break;
            case PetState.Talk:
                // Chat with you while the bubble is up — gentle bob, no wandering.
                _vx = 0; _vy = 0;
                _stateTimer = Math.Max(2.5, _bubbleTimeLeft);
                _anim.Play("talk");
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
