using System.ComponentModel;
using System.Diagnostics;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.App;

internal sealed class OverlayForm : Form
{
    private const double CrouchTransitionDuration = 0.18;
    private const double JumpImpulse = 780;
    private const double JumpHorizontalKick = 55;
    private const double ClimbSpeed = 70;
    private const double ClimbReverseChancePerSecond = 0.15;
    private const double FleeDuration = 4.0;
    private const double FleeJumpChancePerSecond = 0.3;
    private const double CursorEnergyReferenceSpeed = 900;
    private const double EnergySmoothingSeconds = 1.2;
    private const double WallGrabCaptureDistance = 16;
    private const double HideMoveTolerance = 6;
    private const double HideGrabOnMoveChance = 0.25;
    private const double HideMaxReachHeights = 2.0;
    private const double ClimbPoseBlendSeconds = 0.22;
    private const double CoveredHopSpeed = 220;
    private const double MinCharacterScale = 0.7;
    private const double MaxCharacterScale = 1.6;
    private static readonly Size2 BaseCharacterSize = new(60, 114);
    private static readonly Color TransparencyColor = Color.Fuchsia;
    private readonly CharacterStateMachine _stateMachine = new();
    private readonly CharacterPhysics _physics;
    private readonly CharacterRenderer _renderer = new();
    private readonly IWindowPlatformProvider _windowPlatforms;
    private readonly PlatformCollisionResolver _collisionResolver = new();
    private readonly CharacterPlatformAttachment _attachment = new();
    private readonly CharacterPlatformController _platformController;
    private readonly CharacterWallClimb _wallClimb = new();
    private readonly CharacterAttention _attention = new(proximityRadius: 220);
    private readonly CharacterHarassmentTracker _harassment = new();
    private readonly JsonLineLogger _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _previousTicks;
    private double _stateTime;
    private double _animationTime;
    private double _fps;
    private int _frames;
    private double _fpsElapsed;
    private bool _isPaused;
    private bool _isHolding;
    private bool _clickThrough;
    private bool _clicked;
    private Vec2 _mouseDown;
    private Vec2 _previousPointer;
    private long _previousPointerTicks;
    private Vec2 _releaseVelocity;
    private string _lastMouseEvent = "none";
    private string? _currentPlatformId;
    private double _previousBottom;
    private double _currentBottom;
    private bool _showPlatformDebug;
    private CharacterBehaviorTuning _behaviorTuning = CharacterBehaviorTuning.ForIntensity("normal");
    private string _behaviorIntensity = "normal";
    private Vec2 _gazeTarget;
    private double _crouchAmount;
    private double _crouchTransitionStart;
    private double _crouchTransitionElapsed = double.PositiveInfinity;
    private double _climbDirection = 1;
    private double _secondsSinceInteraction = double.MaxValue / 2;
    private double _cursorEnergy;
    private Vec2 _lastFramePointer;
    private bool _isFleeing;
    private double _fleeSecondsRemaining;
    private double _characterScale = 1.0;
    private double _climbAmount;
    private bool _jumpGrabAttempted;
    private string? _hidingPlatformId;
    private RectD _hidingStartBounds;
    private WallSide _hidingSide;

    public OverlayForm(
        JsonLineLogger logger,
        int targetFps,
        IWindowPlatformProvider windowPlatforms)
    {
        _logger = logger;
        _windowPlatforms = windowPlatforms;
        _physics = new CharacterPhysics(new Vec2(120, 60), BaseCharacterSize);
        _platformController = new CharacterPlatformController(_attachment);

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = TransparencyColor;
        TransparencyKey = TransparencyColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        DoubleBuffered = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _stateMachine.StateChanged += OnStateChanged;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(15, 1_000 / Math.Clamp(targetFps, 30, 60)),
        };
        _timer.Tick += (_, _) => UpdateFrame();
        _timer.Start();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            _lastMouseEvent = value ? "paused" : "resumed";
            Invalidate();
        }
    }

    public CharacterState State => _stateMachine.Current;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string BehaviorIntensity
    {
        get => _behaviorIntensity;
        set
        {
            _behaviorIntensity = value;
            _behaviorTuning = CharacterBehaviorTuning.ForIntensity(value);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double CharacterScale
    {
        get => _characterScale;
        set
        {
            _characterScale = Math.Clamp(value, MinCharacterScale, MaxCharacterScale);
            _physics.Rescale(new Size2(
                BaseCharacterSize.Width * _characterScale,
                BaseCharacterSize.Height * _characterScale));
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowPlatformDebug
    {
        get => _showPlatformDebug;
        set
        {
            _showPlatformDebug = value;
            Invalidate();
        }
    }

    public void ShowOverlay()
    {
        Bounds = SystemInformation.VirtualScreen;
        if (!Visible)
        {
            Show();
            _logger.Write("overlay_shown", new { bounds = Bounds.ToString() });
        }

        BringToFront();
    }

    public void HideOverlay()
    {
        Hide();
        _logger.Write("overlay_hidden");
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |=
                NativeWindowStyles.WsExToolWindow |
                NativeWindowStyles.WsExNoActivate |
                NativeWindowStyles.WsExLayered;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ResetForRelease();
        (RectD overlayBounds, RectD virtualBounds) = GetCoordinateSpace();
        _windowPlatforms.Start(overlayBounds, virtualBounds);
        _logger.Write("overlay_created", new
        {
            virtual_screen = SystemInformation.VirtualScreen.ToString(),
            monitor_count = Screen.AllScreens.Length,
        });
    }

    /// <summary>Drops the character in fresh from the top, regardless of whatever state it
    /// drifted into. The timer keeps running even while hidden behind the launcher (see
    /// <see cref="UpdateFrame"/>'s own visibility guard — this is the belt to that
    /// suspenders): without this, a character that autonomously sat down or attached to a
    /// wall before the window was ever shown would appear frozen in mid-air on release,
    /// since nothing re-checks support for a state that isn't Fall.</summary>
    private void ResetForRelease()
    {
        _wallClimb.Stop();
        _attachment.Detach();
        _hidingPlatformId = null;
        _currentPlatformId = null;
        _isFleeing = false;
        _fleeSecondsRemaining = 0;
        _harassment.Reset();
        PositionAtPrimaryScreen();
        _stateMachine.Send(CharacterSignal.SupportLost);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
#if DEBUG
        if (_showPlatformDebug)
        {
            DrawPlatformDebug(e.Graphics);
        }
#endif
        WallSide activeWallSide = _stateMachine.Current == CharacterState.Hide ? _hidingSide : _wallClimb.Side;
        int climbWallDirection = activeWallSide == WallSide.Left ? 1 : -1;
        int hidePeekDirection = activeWallSide == WallSide.Left ? -1 : 1;
        _renderer.Draw(
            e.Graphics,
            _physics.Bounds,
            new CharacterPose(
                _stateMachine.Current,
                _animationTime,
                _clicked,
                _crouchAmount,
                _gazeTarget,
                climbWallDirection,
                ShowShadow: _showPlatformDebug,
                ClimbAmount: _climbAmount,
                HidePeekDirection: hidePeekDirection));

#if DEBUG
        if (_showPlatformDebug)
        {
            DrawDebugPanel(e.Graphics);
        }
#endif
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var pointer = new Vec2(e.X, e.Y);
        if (e.Button != MouseButtons.Left || !GetInteractiveBounds().Inflate(8, 8).Contains(pointer))
        {
            return;
        }

        _isHolding = true;
        _attachment.Detach();
        _wallClimb.Stop();
        _hidingPlatformId = null;
        _currentPlatformId = null;
        Capture = true;
        _mouseDown = pointer;
        _previousPointer = pointer;
        _previousPointerTicks = _clock.ElapsedTicks;
        _releaseVelocity = Vec2.Zero;
        _lastMouseEvent = "grab";
        _secondsSinceInteraction = 0;
        _harassment.RegisterInteraction();
        _stateMachine.Send(CharacterSignal.Grabbed);
        _physics.HoldAt(pointer);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isHolding)
        {
            return;
        }

        var pointer = new Vec2(e.X, e.Y);
        long now = _clock.ElapsedTicks;
        double elapsed = (now - _previousPointerTicks) / (double)Stopwatch.Frequency;
        if (elapsed > 0.004)
        {
            _releaseVelocity = ((pointer - _previousPointer) * (1 / elapsed)).ClampMagnitude(1_600);
            _previousPointer = pointer;
            _previousPointerTicks = now;
        }

        _physics.HoldAt(pointer);
        _lastMouseEvent = "drag";
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_isHolding || e.Button != MouseButtons.Left)
        {
            return;
        }

        var pointer = new Vec2(e.X, e.Y);
        bool wasClick = (pointer - _mouseDown).Length < 8;
        _isHolding = false;
        Capture = false;
        _stateMachine.Send(CharacterSignal.Released);

        if (wasClick)
        {
            _physics.Nudge(new Vec2(Random.Shared.Next(-170, 171), -520));
            _clicked = true;
            _lastMouseEvent = "click reaction";
        }
        else
        {
            _physics.Release(_releaseVelocity);
            _lastMouseEvent = "throw";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateFrame()
    {
        if (!Visible)
        {
            // Nothing to simulate while the character hasn't been released yet (or has been
            // hidden again) — otherwise it keeps walking/sitting/running on the invisible
            // desktop floor the whole time the launcher sits open, and shows up already deep
            // into some unrelated state the moment it's finally released.
            _previousTicks = 0;
            return;
        }

        long currentTicks = _clock.ElapsedTicks;
        if (_previousTicks == 0)
        {
            _previousTicks = currentTicks;
            return;
        }

        double delta = (currentTicks - _previousTicks) / (double)Stopwatch.Frequency;
        _previousTicks = currentTicks;
        UpdateFps(delta);
        (RectD overlayBounds, RectD virtualBounds) = GetCoordinateSpace();
        _windowPlatforms.Pump(DateTimeOffset.UtcNow, overlayBounds, virtualBounds);

        Point clientPointer = IsHandleCreated ? PointToClient(Cursor.Position) : Point.Empty;
        var pointerOverlay = new Vec2(clientPointer.X, clientPointer.Y);

        if (!_isPaused)
        {
            _animationTime += delta;
            _stateTime += delta;
            _secondsSinceInteraction += delta;
            UpdateCrouchAmount(delta);
            UpdateClimbAmount(delta);
            UpdateCursorEnergy(delta, pointerOverlay);

            Vec2 characterCenter = new(
                _physics.Position.X + (_physics.Size.Width / 2),
                _physics.Position.Y + (_physics.Size.Height / 2));
            double distanceToCursor = (characterCenter - pointerOverlay).Length;
            _harassment.Update(delta, distanceToCursor);
            UpdateFleeBehavior(delta, characterCenter, pointerOverlay);
            _gazeTarget = _attention.ShouldTrackCursor(_animationTime, _secondsSinceInteraction, distanceToCursor)
                ? pointerOverlay
                : NeutralGazeTarget();

            PlatformSnapshot snapshot = _windowPlatforms.Snapshot;
            if (_stateMachine.Current == CharacterState.Climb)
            {
                UpdateClimb(delta, snapshot);
            }
            else if (_stateMachine.Current == CharacterState.Hide)
            {
                UpdateHiding(snapshot);
            }
            else
            {
                UpdateGroundedPhysics(delta, snapshot);
            }

            UpdateAutonomousBehavior();
            if (_clicked && _stateTime > 0.45)
            {
                _clicked = false;
            }
        }

        UpdateClickThrough(clientPointer);
        Invalidate();
    }

    private void UpdateGroundedPhysics(double delta, PlatformSnapshot snapshot)
    {
        DesktopPlatform? attachedPlatform = FollowAttachedPlatform(snapshot);
        (double left, double right, DesktopPlatform? leftWall, DesktopPlatform? rightWall) =
            GetHorizontalBoundaries(attachedPlatform, snapshot);
        CharacterMotionStep motion = _physics.Integrate(delta, _stateMachine.Current, left, right);
        _previousBottom = motion.PreviousBounds.Bottom;
        _currentBottom = motion.CurrentBounds.Bottom;

        if (_stateMachine.Current == CharacterState.Fall)
        {
            if (_physics.Velocity.Y < 0)
            {
                if (!_jumpGrabAttempted)
                {
                    WallGrabDetector.Reach? reach = WallGrabDetector.FindReachableEdge(
                        motion.CurrentBounds,
                        _physics.Velocity.X,
                        snapshot.Platforms,
                        WallGrabCaptureDistance);
                    if (reach is not null)
                    {
                        _jumpGrabAttempted = true;
                        if (Random.Shared.NextDouble() < _behaviorTuning.GrabChance)
                        {
                            StartClimb(reach.Value.Platform, reach.Value.Side, initialDirection: -1);
                            return;
                        }
                    }
                }

                PlatformCollision? ceiling = _collisionResolver.ResolveUpward(
                    motion.PreviousBounds,
                    motion.CurrentBounds,
                    _physics.Velocity.Y,
                    snapshot.Platforms);
                if (ceiling is not null)
                {
                    _physics.BonkCeiling(ceiling.Value.Segment.SurfaceY);
                    _logger.Write("character_bonked_ceiling", new { platform = ceiling.Value.Platform.Id });
                }

                return;
            }

            var collisionPlatforms = new List<DesktopPlatform>(snapshot.Platforms.Length + 1);
            collisionPlatforms.AddRange(snapshot.Platforms);
            collisionPlatforms.Add(CreateDesktopPlatform());
            PlatformCollision? collision = _collisionResolver.ResolveDownward(
                motion.PreviousBounds,
                motion.CurrentBounds,
                _physics.Velocity.Y,
                collisionPlatforms);
            if (collision is not null)
            {
                _physics.LandOn(collision.Value.Segment.SurfaceY);
                _currentPlatformId = collision.Value.Platform.Id;
                if (collision.Value.Platform.Kind == PlatformKind.Window)
                {
                    _attachment.Attach(collision.Value.Platform, _physics.Bounds);
                }
                else
                {
                    _attachment.Detach();
                }

                _logger.Write("character_landed_on_platform", new
                {
                    platform = collision.Value.Platform.Id,
                    kind = collision.Value.Platform.Kind.ToString(),
                    surface_y = collision.Value.Segment.SurfaceY,
                });
                _stateMachine.Send(CharacterSignal.Landed);
            }
        }
        else if (attachedPlatform is not null &&
                 _stateMachine.Current is CharacterState.Idle or CharacterState.Walk
                     or CharacterState.Run or CharacterState.Sit)
        {
            if (motion.HitHorizontalEdge &&
                _stateMachine.Current is CharacterState.Walk or CharacterState.Run &&
                attachedPlatform.Kind == PlatformKind.Window)
            {
                DesktopPlatform? neighborWall = _physics.WalkDirection > 0 ? rightWall : leftWall;
                if (neighborWall is not null)
                {
                    HandleWallEncounter(neighborWall, WallSideResolver.ForEncounteredWall(_physics.WalkDirection));
                    return;
                }

                if (Random.Shared.NextDouble() < _behaviorTuning.ClimbChance)
                {
                    StartClimb(attachedPlatform, WallSideResolver.ForOwnEdge(_physics.WalkDirection));
                    return;
                }
            }

            PlatformSegment? support = _attachment.FindSupportingSegment(
                attachedPlatform,
                _physics.Bounds);
            if (support is null)
            {
                LosePlatformSupport();
            }
            else
            {
                _physics.SetPosition(new Vec2(
                    _physics.Position.X,
                    support.Value.SurfaceY - _physics.Size.Height - _attachment.VerticalOffset));
                _attachment.Sync(attachedPlatform, _physics.Bounds);
            }
        }
    }

    private void StartClimb(DesktopPlatform platform, WallSide side, double initialDirection = 1)
    {
        _wallClimb.Start(platform, side, _physics.Size);
        _climbDirection = initialDirection;
        _attachment.Detach();
        _currentPlatformId = null;
        _stateMachine.Send(CharacterSignal.ClimbRequested);
        _logger.Write("character_started_climbing", new
        {
            platform = platform.Id,
            side = side.ToString(),
            direction = initialDirection,
        });
    }

    /// <summary>A window other than the one being walked on rises up across the walking
    /// line ahead — with some chance climb up its near face, with some chance duck out of
    /// sight behind it, otherwise it's just a wall and the bounce that already happened
    /// this frame (via the tightened boundary from <see cref="GetHorizontalBoundaries"/>)
    /// is the whole reaction.</summary>
    private void HandleWallEncounter(DesktopPlatform wall, WallSide side)
    {
        double roll = Random.Shared.NextDouble();
        if (roll < _behaviorTuning.ClimbChance)
        {
            StartClimb(wall, side, initialDirection: -1);
            return;
        }

        // Hiding snaps the character up to the wall's own top edge so its head can peek
        // above it — fine for a nearby ledge, but a tall wall's top could be many screen
        // heights away. Only offer it when that hop stays plausible; a too-tall wall just
        // falls through to a plain bounce instead.
        bool closeEnoughToHide =
            Math.Abs(wall.Bounds.Y - _physics.Bounds.Bottom) <= _physics.Size.Height * HideMaxReachHeights;
        if (closeEnoughToHide && roll < _behaviorTuning.ClimbChance + _behaviorTuning.HideChance)
        {
            EnterHide(wall, side);
        }
    }

    private void EnterHide(DesktopPlatform wall, WallSide side)
    {
        double headSize = _physics.Size.Width * 0.52;
        double bodyY = wall.Bounds.Y - (headSize * 0.5);
        double bodyX = side == WallSide.Left
            ? wall.Bounds.X - (_physics.Size.Width * 0.5)
            : wall.Bounds.Right - (_physics.Size.Width * 0.5);
        _physics.SetPosition(new Vec2(bodyX, bodyY));
        _hidingPlatformId = wall.Id;
        _hidingStartBounds = wall.Bounds;
        _hidingSide = side;
        _attachment.Detach();
        _currentPlatformId = null;
        _stateMachine.Send(CharacterSignal.HideRequested);
        _logger.Write("character_hiding", new { wall = wall.Id, side = side.ToString() });
    }

    private void UpdateHiding(PlatformSnapshot snapshot)
    {
        DesktopPlatform? wall = snapshot.Platforms.FirstOrDefault(p => p.Id == _hidingPlatformId);
        if (wall is null)
        {
            _stateMachine.Send(CharacterSignal.SupportLost);
            return;
        }

        bool moved = Math.Abs(wall.Bounds.X - _hidingStartBounds.X) > HideMoveTolerance ||
            Math.Abs(wall.Bounds.Y - _hidingStartBounds.Y) > HideMoveTolerance;
        if (!moved)
        {
            return;
        }

        if (Random.Shared.NextDouble() < HideGrabOnMoveChance)
        {
            StartClimb(wall, _hidingSide);
        }
        else
        {
            _stateMachine.Send(CharacterSignal.SupportLost);
        }
    }

    private void UpdateClimb(double delta, PlatformSnapshot snapshot)
    {
        DesktopPlatform? platform = snapshot.Platforms.FirstOrDefault(p => p.Id == _wallClimb.PlatformId);
        if (platform is null)
        {
            _wallClimb.Stop();
            _stateMachine.Send(CharacterSignal.SupportLost);
            return;
        }

        if (_climbDirection > 0 && Random.Shared.NextDouble() < ClimbReverseChancePerSecond * delta)
        {
            _climbDirection = -1;
        }

        ClimbStep step = _wallClimb.Advance(_physics.Position.Y, _climbDirection * ClimbSpeed * delta);
        _physics.SetPosition(step.Position);

        if (step.Outcome == ClimbOutcome.ReachedTop)
        {
            // Advance() already eases the position onto the ledge (flush with the edge)
            // as it nears the top, so by the time ReachedTop fires there's nothing left to
            // snap — attaching right where it already is just works.
            _wallClimb.Stop();
            _attachment.Attach(platform, _physics.Bounds);
            _currentPlatformId = platform.Id;
            _stateMachine.Send(CharacterSignal.Landed);
        }
        else if (step.Outcome == ClimbOutcome.ReachedBottom)
        {
            _wallClimb.Stop();
            _stateMachine.Send(CharacterSignal.SupportLost);
        }
    }

    private void UpdateAutonomousBehavior()
    {
        switch (_stateMachine.Current)
        {
            case CharacterState.Idle when _stateTime >= _behaviorTuning.IdleDelay:
                CharacterSignal signal = _behaviorTuning.PickAutonomousTransition(Random.Shared.NextDouble());
                if (signal == CharacterSignal.JumpRequested)
                {
                    TryJump();
                }
                else
                {
                    _stateMachine.Send(signal);
                }

                break;
            case CharacterState.Walk when _stateTime >= _behaviorTuning.WalkDuration:
                _stateMachine.Send(CharacterSignal.StopRequested);
                break;
            case CharacterState.Run when _stateTime >= _behaviorTuning.RunDuration:
                _stateMachine.Send(CharacterSignal.StopRequested);
                break;
            case CharacterState.Sit when _stateTime >= _behaviorTuning.SitDuration:
                _stateMachine.Send(CharacterSignal.StandRequested);
                break;
        }
    }

    private void TryJump()
    {
        if (!_stateMachine.Send(CharacterSignal.JumpRequested))
        {
            return;
        }

        _physics.Nudge(new Vec2(_physics.WalkDirection * JumpHorizontalKick, -JumpImpulse));
        _logger.Write("character_jumped");
    }

    private void UpdateFleeBehavior(double delta, Vec2 characterCenter, Vec2 pointerOverlay)
    {
        bool wasFleeing = _isFleeing;
        if (_harassment.IsFleeing)
        {
            _isFleeing = true;
            _fleeSecondsRemaining = FleeDuration;
        }
        else if (_isFleeing)
        {
            _fleeSecondsRemaining -= delta;
            if (_fleeSecondsRemaining <= 0)
            {
                _isFleeing = false;
            }
        }

        if (!_isFleeing)
        {
            return;
        }

        if (!wasFleeing)
        {
            _logger.Write("character_fleeing_started");
        }

        switch (_stateMachine.Current)
        {
            case CharacterState.Sit:
                _stateMachine.Send(CharacterSignal.StandRequested);
                break;
            case CharacterState.Idle or CharacterState.Walk:
                _stateMachine.Send(CharacterSignal.RunRequested);
                break;
        }

        if (_stateMachine.Current is CharacterState.Run or CharacterState.Walk)
        {
            _physics.FaceDirection(characterCenter.X >= pointerOverlay.X ? 1 : -1);
            if (Random.Shared.NextDouble() < FleeJumpChancePerSecond * delta)
            {
                TryJump();
            }
        }
    }

    private void UpdateCursorEnergy(double delta, Vec2 pointerOverlay)
    {
        if (delta > 0)
        {
            double speed = (pointerOverlay - _lastFramePointer).Length / delta;
            double normalized = Math.Clamp(speed / CursorEnergyReferenceSpeed, 0, 1);
            double smoothing = Math.Clamp(delta / EnergySmoothingSeconds, 0, 1);
            _cursorEnergy += (normalized - _cursorEnergy) * smoothing;
        }

        _lastFramePointer = pointerOverlay;
        _behaviorTuning = CharacterBehaviorTuning.ForEnergy(_behaviorIntensity, _cursorEnergy);
    }

    private Vec2 NeutralGazeTarget() => new(
        _physics.Position.X + (_physics.Size.Width / 2),
        _physics.Position.Y + (_physics.Size.Height * 0.25));

    private void UpdateCrouchAmount(double delta)
    {
        if (_stateMachine.Current == CharacterState.Sit)
        {
            _crouchAmount = 1;
            _crouchTransitionElapsed = double.PositiveInfinity;
            return;
        }

        if (_crouchTransitionElapsed >= CrouchTransitionDuration)
        {
            _crouchAmount = 0;
            return;
        }

        _crouchTransitionElapsed += delta;
        double t = Math.Clamp(_crouchTransitionElapsed / CrouchTransitionDuration, 0, 1);
        _crouchAmount = _crouchTransitionStart * (1 - t);
    }

    /// <summary>Eases the renderer's arm pose toward "climbing" while in <see
    /// cref="CharacterState.Climb"/> and back toward the normal pose otherwise, so entering
    /// or leaving a climb cross-fades the limbs instead of swapping poses on a single frame.</summary>
    private void UpdateClimbAmount(double delta)
    {
        double target = _stateMachine.Current == CharacterState.Climb ? 1 : 0;
        double step = delta / ClimbPoseBlendSeconds;
        _climbAmount = target > _climbAmount
            ? Math.Min(target, _climbAmount + step)
            : Math.Max(target, _climbAmount - step);
    }

    private DesktopPlatform? FollowAttachedPlatform(PlatformSnapshot snapshot)
    {
        bool attached = _platformController.TryFollow(
            snapshot,
            _physics,
            _stateMachine,
            out DesktopPlatform? platform,
            out string? lostPlatform);
        if (lostPlatform is not null)
        {
            _currentPlatformId = null;
            _logger.Write("character_platform_lost", new { platform = lostPlatform });
        }

        _currentPlatformId = attached ? platform!.Id : _currentPlatformId;
        return attached ? platform : null;
    }

    private (double Left, double Right, DesktopPlatform? LeftWall, DesktopPlatform? RightWall) GetHorizontalBoundaries(
        DesktopPlatform? attachedPlatform,
        PlatformSnapshot snapshot)
    {
        if (attachedPlatform is not null)
        {
            PlatformSegment? segment = _attachment.FindSupportingSegment(
                attachedPlatform,
                _physics.Bounds);
            if (segment is not null)
            {
                const double footWidthRatio = 0.42;
                double overhang = (_physics.Size.Width - (_physics.Size.Width * footWidthRatio)) / 2;
                double left = segment.Value.Left - overhang;
                double right = segment.Value.Right + overhang;

                // A different window standing in the way is a wall too, and must stop
                // horizontal motion at its own edge even if the walked-on platform's
                // (occlusion-clipped) surface segment would technically continue past it.
                WallEncounterDetector.Neighbors neighbors = WallEncounterDetector.FindNeighborWalls(
                    attachedPlatform,
                    _physics.Bounds,
                    snapshot.Platforms);
                DesktopPlatform? leftWall = null;
                DesktopPlatform? rightWall = null;
                if (neighbors.Left is { } leftNeighbor && leftNeighbor.Boundary > left)
                {
                    left = leftNeighbor.Boundary;
                    leftWall = leftNeighbor.Platform;
                }

                if (neighbors.Right is { } rightNeighbor && rightNeighbor.Boundary < right)
                {
                    right = rightNeighbor.Boundary;
                    rightWall = rightNeighbor.Platform;
                }

                return (left, right, leftWall, rightWall);
            }
        }

        (_, double workLeft, double workRight) = GetCurrentWorkArea();
        return (workLeft, workRight, null, null);
    }

    private DesktopPlatform CreateDesktopPlatform()
    {
        (double floor, double left, double right) = GetCurrentWorkArea();
        return new DesktopPlatform
        {
            Id = "desktop:work-area",
            Kind = PlatformKind.Desktop,
            Bounds = new RectD(left, floor, right - left, 1),
            Segments = [new PlatformSegment(left, right, floor)],
            ZOrder = int.MaxValue,
            MonitorId = "work-area",
            MonitorTop = GetCurrentMonitorTop(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void LosePlatformSupport()
    {
        if (!_attachment.IsAttached)
        {
            return;
        }

        string? lostPlatform = _attachment.PlatformId;
        // Support usually disappears because the platform itself moved/shrank/closed. But if
        // some other window now overlaps right where the character is standing, it got
        // covered instead — a startled little hop reads much better than silently vanishing
        // underneath whatever just slid over it.
        bool covered = _windowPlatforms.Snapshot.Platforms.Any(platform =>
            platform.Kind == PlatformKind.Window &&
            platform.Id != lostPlatform &&
            platform.Bounds.Intersects(_physics.Bounds));
        _attachment.Detach();
        _currentPlatformId = null;
        _stateMachine.Send(CharacterSignal.SupportLost);
        if (covered)
        {
            _physics.Nudge(new Vec2(_physics.Velocity.X, -CoveredHopSpeed));
            _logger.Write("character_covered_by_window", new { platform = lostPlatform });
        }
        else
        {
            _logger.Write("character_platform_lost", new { platform = lostPlatform });
        }
    }

    private (RectD OverlayBounds, RectD VirtualBounds) GetCoordinateSpace()
    {
        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        return (
            new RectD(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height),
            new RectD(
                virtualScreen.X,
                virtualScreen.Y,
                virtualScreen.Width,
                virtualScreen.Height));
    }

    private void UpdateClickThrough(Point clientPointer)
    {
        if (!IsHandleCreated || !Visible)
        {
            return;
        }

        bool overCharacter = GetInteractiveBounds().Inflate(8, 8).Contains(
            new Vec2(clientPointer.X, clientPointer.Y));
        bool shouldClickThrough = !_isHolding && !overCharacter;
        if (shouldClickThrough == _clickThrough)
        {
            return;
        }

        _clickThrough = shouldClickThrough;
        NativeWindowStyles.SetClickThrough(Handle, shouldClickThrough);
    }

    private (double Floor, double Left, double Right) GetCurrentWorkArea()
    {
        Rectangle workArea = Screen.FromPoint(CharacterCenter()).WorkingArea;
        return (
            workArea.Bottom - Bounds.Top,
            workArea.Left - Bounds.Left,
            workArea.Right - Bounds.Left);
    }

    private double GetCurrentMonitorTop() =>
        Screen.FromPoint(CharacterCenter()).Bounds.Top - Bounds.Top;

    /// <summary>The area that actually reads as "the character" for click/hover purposes.
    /// While hiding, most of <see cref="CharacterPhysics.Bounds"/> is empty air — only the
    /// head is drawn — so hit-testing the full body would grab/click-block on nothing
    /// visible.</summary>
    private RectD GetInteractiveBounds()
    {
        if (_stateMachine.Current != CharacterState.Hide)
        {
            return _physics.Bounds;
        }

        double headSize = _physics.Size.Width * 0.52;
        return new RectD(_physics.Position.X, _physics.Position.Y, _physics.Size.Width, headSize);
    }

    private Point CharacterCenter() => new(
        Bounds.Left + (int)(_physics.Position.X + (_physics.Size.Width / 2)),
        Bounds.Top + (int)(_physics.Position.Y + (_physics.Size.Height / 2)));

    private void PositionAtPrimaryScreen()
    {
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        var start = new Vec2(
            work.Left - Bounds.Left + ((work.Width - _physics.Size.Width) / 2),
            work.Top - Bounds.Top + 30);
        _physics.HoldAt(start + new Vec2(_physics.Size.Width / 2, _physics.Size.Height / 2));
        _physics.Release(Vec2.Zero);
    }

    private void OnStateChanged(
        CharacterState previous,
        CharacterState current,
        CharacterSignal signal)
    {
        _stateTime = 0;
        if (signal == CharacterSignal.Landed)
        {
            _crouchTransitionStart = 0.6;
            _crouchTransitionElapsed = 0;
        }
        else if (previous == CharacterState.Sit && current != CharacterState.Sit)
        {
            _crouchTransitionStart = 1;
            _crouchTransitionElapsed = 0;
        }

        if (current == CharacterState.Fall && previous != CharacterState.Fall)
        {
            _jumpGrabAttempted = false;
        }

        _logger.Write("character_state_changed", new
        {
            previous = previous.ToString(),
            current = current.ToString(),
            signal = signal.ToString(),
        });
    }

    private void UpdateFps(double delta)
    {
        _frames++;
        _fpsElapsed += delta;
        if (_fpsElapsed < 0.5)
        {
            return;
        }

        _fps = _frames / _fpsElapsed;
        _frames = 0;
        _fpsElapsed = 0;
    }

#if DEBUG
    private void DrawPlatformDebug(Graphics graphics)
    {
        using var boundsPen = new Pen(Color.FromArgb(150, 68, 204, 255), 1)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };
        using var surfacePen = new Pen(Color.FromArgb(230, 255, 196, 64), 3);
        using var labelBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        using var labelBackground = new SolidBrush(Color.FromArgb(180, 24, 27, 37));

        foreach (DesktopPlatform platform in _windowPlatforms.Snapshot.Platforms)
        {
            RectD bounds = platform.Bounds;
            graphics.DrawRectangle(
                boundsPen,
                (float)bounds.X,
                (float)bounds.Y,
                (float)bounds.Width,
                (float)bounds.Height);
            foreach (PlatformSegment segment in platform.Segments)
            {
                graphics.DrawLine(
                    surfacePen,
                    (float)segment.Left,
                    (float)segment.SurfaceY,
                    (float)segment.Right,
                    (float)segment.SurfaceY);
            }

            string label = $"{platform.Id}  HWND 0x{platform.ExternalHandle:X}";
            SizeF labelSize = graphics.MeasureString(label, Font);
            graphics.FillRectangle(
                labelBackground,
                (float)bounds.X,
                (float)bounds.Y - labelSize.Height,
                labelSize.Width + 6,
                labelSize.Height);
            graphics.DrawString(
                label,
                Font,
                labelBrush,
                (float)bounds.X + 3,
                (float)bounds.Y - labelSize.Height);
        }

        (double footLeft, double footRight) = _collisionResolver.GetFootInterval(_physics.Bounds);
        using var footPen = new Pen(Color.LimeGreen, 4);
        graphics.DrawLine(
            footPen,
            (float)footLeft,
            (float)_physics.Bounds.Bottom,
            (float)footRight,
            (float)_physics.Bounds.Bottom);

        if (_attachment.IsAttached)
        {
            using var attachmentBrush = new SolidBrush(Color.HotPink);
            float x = (float)(_attachment.LastPlatformBounds.X + _attachment.RelativeFootCenterX);
            float y = (float)_physics.Bounds.Bottom;
            graphics.FillEllipse(attachmentBrush, x - 5, y - 5, 10, 10);
        }
    }

    private void DrawDebugPanel(Graphics graphics)
    {
        const int width = 260;
        const int height = 280;
        using var background = new SolidBrush(Color.FromArgb(220, 27, 29, 40));
        using var text = new SolidBrush(Color.White);
        graphics.FillRectangle(background, 14, 14, width, height);

        string details =
            $"DesktopPeople • DEBUG\n" +
            $"FPS: {_fps:F0}\n" +
            $"State: {_stateMachine.Current}\n" +
            $"Velocity: {_physics.Velocity.X:F0}, {_physics.Velocity.Y:F0}\n" +
            $"Platform: {_currentPlatformId ?? "none"}\n" +
            $"Windows: {_windowPlatforms.Snapshot.Platforms.Length}\n" +
            $"Attached: {_attachment.IsAttached}\n" +
            $"Sweep Y: {_previousBottom:F0} → {_currentBottom:F0}\n" +
            $"Mouse: {_lastMouseEvent}\n" +
            $"Intensity: {_behaviorIntensity}  Energy: {_cursorEnergy:F2}\n" +
            $"Harassment: {_harassment.Level:F1}  Fleeing: {_isFleeing}\n" +
            $"Climbing: {_wallClimb.IsClimbing}  Hiding: {_hidingPlatformId ?? "-"}\n" +
            $"Scale: {_characterScale:F2}\n" +
            $"Clip: {_stateMachine.Current.ToString().ToLowerInvariant()}";
        graphics.DrawString(details, Font, text, new RectangleF(26, 24, width - 20, height - 16));

        using var hitbox = new Pen(Color.LimeGreen, 1);
        RectD bounds = GetInteractiveBounds();
        graphics.DrawRectangle(
            hitbox,
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height);
    }
#endif
}
