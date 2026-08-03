using System.Collections.Immutable;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.Core;

/// <summary>
/// Everything the desktop character actually *does*: physics, the state machine, walking,
/// climbing, hiding, fleeing, attention and the recovery guards around all of it.
/// <para>
/// This used to live inside the WinForms <c>OverlayForm</c>, which meant roughly nine
/// hundred lines of behaviour could not be exercised by a single unit test — every bug in
/// it (characters falling through the floor, infinite bouncing, arms pawing at the air, the
/// unclosable window) could only be found by a human running the app and watching. Hosts
/// now own only their window, input and painting; all of this is host-agnostic and testable.
/// </para>
/// </summary>
public sealed class CharacterSimulation
{
    private const double CrouchTransitionDuration = 0.18;
    private const double JumpImpulse = 780;
    private const double JumpHorizontalKick = 55;
    private const double ClimbSpeed = 70;
    private const double ClimbReverseChancePerSecond = 0.15;
    private const double ClimbLetGoChancePerSecond = 0.05;
    private const double ClimbPushOffChancePerSecond = 0.04;
    private const double ClimbPushOffHorizontalSpeed = 260;
    private const double ClimbPushOffVerticalImpulse = 560;
    private const string LeftScreenEdgeId = "screen:left-edge";
    private const string RightScreenEdgeId = "screen:right-edge";
    private const double FleeDuration = 4.0;
    private const double FleeJumpChancePerSecond = 0.3;
    private const double CursorEnergyReferenceSpeed = 900;
    private const double EnergySmoothingSeconds = 1.2;
    private const double WallGrabCaptureDistance = 16;
    private const double HideMoveTolerance = 6;
    private const double HideLowerOffset = 0.14;
    private const double HideGrabOnMoveChance = 0.25;
    private const double ClimbPoseBlendSeconds = 0.22;
    private const double HidePoseBlendSeconds = 0.28;
    private const double PlatformMissingGraceSeconds = 0.4;
    private const double CoveredHopSpeed = 220;
    private const double MinCharacterScale = 0.7;
    private const double MaxCharacterScale = 1.6;
    private static readonly Size2 BaseCharacterSize = new(60, 114);
    private readonly CharacterStateMachine _stateMachine = new();
    private readonly CharacterPhysics _physics;
    private readonly IWindowPlatformProvider _windowPlatforms;
    private readonly IScreenGeometry _screens;
    private readonly PlatformCollisionResolver _collisionResolver = new();
    private readonly CharacterPlatformAttachment _attachment = new();
    private readonly CharacterPlatformController _platformController;
    private readonly CharacterWallClimb _wallClimb = new();
    private readonly CharacterAttention _attention = new(proximityRadius: 220);
    private readonly CharacterHarassmentTracker _harassment = new();
    private readonly IOverlayLogger _logger;
    private double _stateTime;
    private double _animationTime;
    private bool _isPaused;
    private bool _isHolding;
    private bool _clicked;
    private Vec2 _mouseDown;
    private Vec2 _previousPointer;
    private double _previousPointerSeconds;
    private Vec2 _releaseVelocity;
    private string? _currentPlatformId;
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
    private double _hideAmount;
    private Vec2 _hideEntryPosition;
    private double _climbPlatformMissingSeconds;
    private double _hidingPlatformMissingSeconds;
    private bool _pressedAgainstWall;

    public CharacterSimulation(
        IOverlayLogger logger,
        IWindowPlatformProvider windowPlatforms,
        IScreenGeometry screens)
    {
        _logger = logger;
        _windowPlatforms = windowPlatforms;
        _screens = screens;
        _physics = new CharacterPhysics(new Vec2(120, 60), BaseCharacterSize);
        _platformController = new CharacterPlatformController(_attachment);
        _stateMachine.StateChanged += OnStateChanged;
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => _isPaused = value;
    }

    public CharacterState State => _stateMachine.Current;

    public string BehaviorIntensity
    {
        get => _behaviorIntensity;
        set
        {
            _behaviorIntensity = value;
            _behaviorTuning = CharacterBehaviorTuning.ForIntensity(value);
        }
    }

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

    /// <summary>Whether the character is currently held by the pointer — hosts use this to
    /// decide that the overlay must keep swallowing mouse input even outside its silhouette.</summary>
    public bool IsHeld => _isHolding;

    /// <summary>The area that reads as "the character" for click/hover purposes — narrower
    /// than the physics body while hiding, where most of it is empty air.</summary>
    public RectD InteractiveBounds => GetInteractiveBounds();

    /// <summary>Read-only internals for the developer overlay. Exposed as one snapshot so the
    /// debug panel does not become a reason to widen access to the simulation's own state.</summary>
    public CharacterDiagnostics Diagnostics() => new(
        _stateMachine.Current,
        _physics.Velocity,
        _physics.Bounds,
        _collisionResolver.GetFootInterval(_physics.Bounds),
        _currentPlatformId,
        _windowPlatforms.Snapshot.Platforms.Length,
        _attachment.IsAttached,
        _attachment.IsAttached
            ? _attachment.LastPlatformBounds.X + _attachment.RelativeFootCenterX
            : null,
        _behaviorIntensity,
        _cursorEnergy,
        _harassment.Level,
        _isFleeing,
        _wallClimb.IsClimbing,
        _hidingPlatformId,
        _characterScale);

    /// <summary>Snapshot of everything a renderer needs for this frame, with no reference to
    /// any drawing API — the seam that lets the GDI+ renderer, a Godot renderer, and later a
    /// photo-built avatar rig all consume the same simulation.</summary>
    public CharacterFrame CurrentFrame()
    {
        WallSide activeWallSide = _stateMachine.Current == CharacterState.Hide ? _hidingSide : _wallClimb.Side;

        // While easing into Hide the physics position has already jumped to the final tucked
        // spot (hit-testing and collision have to be consistent immediately); only the drawn
        // position slides across, so the character visibly settles into the corner instead of
        // teleporting on the first Hide frame.
        RectD body = _physics.Bounds;
        if (_stateMachine.Current == CharacterState.Hide && _hideAmount < 1)
        {
            body = new RectD(
                _hideEntryPosition.X + ((_physics.Position.X - _hideEntryPosition.X) * _hideAmount),
                _hideEntryPosition.Y + ((_physics.Position.Y - _hideEntryPosition.Y) * _hideAmount),
                _physics.Size.Width,
                _physics.Size.Height);
        }

        return new CharacterFrame(
            _stateMachine.Current,
            body,
            _animationTime,
            _clicked,
            _crouchAmount,
            _gazeTarget,
            ClimbWallDirection: activeWallSide == WallSide.Left ? 1 : -1,
            HidePeekDirection: activeWallSide == WallSide.Left ? -1 : 1,
            ClimbAmount: _climbAmount,
            HideAmount: _hideAmount,
            HidingWallBounds: HidingWallBounds());
    }

    /// <summary>The wall the character is tucked behind, if any. Hosts clip the character
    /// against it: the overlay is always drawn on top of every real window, so the only way
    /// to read as "behind" is to not paint the hidden part.</summary>
    private RectD? HidingWallBounds()
    {
        if (_stateMachine.Current != CharacterState.Hide || _hidingPlatformId is null)
        {
            return null;
        }

        DesktopPlatform? wall = _windowPlatforms.Snapshot.Platforms
            .FirstOrDefault(p => p.Id == _hidingPlatformId);
        return wall?.Bounds;
    }

    /// <summary>Called once the host's window exists and its geometry is known.</summary>
    public void Start(RectD overlayBounds, RectD virtualBounds)
    {
        ResetForRelease();
        _windowPlatforms.Start(overlayBounds, virtualBounds);
        _logger.Write("overlay_created", new
        {
            virtual_screen = virtualBounds.ToString(),
            monitor_count = _screens.MonitorCount,
        });
    }

    /// <summary>Drops the character in fresh from the top, regardless of whatever state it
    /// drifted into. Hosts keep ticking the simulation even while the character is hidden
    /// behind the launcher (see <see cref="Update"/>'s own guard — this is the belt to that
    /// suspenders): without this, a character that autonomously sat down or attached to a
    /// wall before the window was ever shown would appear frozen in mid-air on release,
    /// since nothing re-checks support for a state that isn't Fall.</summary>
    public void ResetForRelease()
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

    /// <summary>Pointer pressed. Returns whether the character was actually grabbed, which
    /// hosts need in order to decide whether to capture the mouse.</summary>
    public bool TryGrab(Vec2 pointer, double timestampSeconds)
    {
        if (!GetInteractiveBounds().Inflate(8, 8).Contains(pointer))
        {
            return false;
        }

        _isHolding = true;
        _attachment.Detach();
        _wallClimb.Stop();
        _hidingPlatformId = null;
        _currentPlatformId = null;
        _mouseDown = pointer;
        _previousPointer = pointer;
        _previousPointerSeconds = timestampSeconds;
        _releaseVelocity = Vec2.Zero;
        _secondsSinceInteraction = 0;
        _harassment.RegisterInteraction();
        _stateMachine.Send(CharacterSignal.Grabbed);
        _physics.HoldAt(pointer);
        return true;
    }

    /// <summary>Pointer moved while held. The running velocity estimate here is what gets
    /// handed to the physics on release, so a flick actually throws the character.</summary>
    public void Drag(Vec2 pointer, double timestampSeconds)
    {
        if (!_isHolding)
        {
            return;
        }

        double elapsed = timestampSeconds - _previousPointerSeconds;
        if (elapsed > 0.004)
        {
            _releaseVelocity = ((pointer - _previousPointer) * (1 / elapsed)).ClampMagnitude(1_600);
            _previousPointer = pointer;
            _previousPointerSeconds = timestampSeconds;
        }

        _physics.HoldAt(pointer);
    }

    /// <summary>Pointer released. A short press reads as a click (the character reacts with a
    /// startled hop); anything longer is a throw carrying the drag velocity.</summary>
    public void ReleaseGrab(Vec2 pointer)
    {
        if (!_isHolding)
        {
            return;
        }

        bool wasClick = (pointer - _mouseDown).Length < 8;
        _isHolding = false;
        _stateMachine.Send(CharacterSignal.Released);

        if (wasClick)
        {
            _physics.Nudge(new Vec2(Random.Shared.Next(-170, 171), -520));
            _clicked = true;
        }
        else
        {
            _physics.Release(_releaseVelocity);
        }
    }

    /// <summary>Whether the pointer is over the character right now — hosts use this to turn
    /// click-through on and off so clicks land on the desktop everywhere else.</summary>
    public bool HitTest(Vec2 pointer) => GetInteractiveBounds().Inflate(8, 8).Contains(pointer);

    /// <summary>Advances the whole simulation by one frame. Hosts are expected to have
    /// pumped <see cref="IWindowPlatformProvider"/> for this frame already — that needs the
    /// host window's own screen-space geometry to map real windows into overlay coordinates,
    /// which is squarely a host concern.</summary>
    /// <param name="visible">Hosts pass false while the character is hidden behind the
    /// launcher: the simulation must not keep walking around on an invisible floor and then
    /// pop up mid-behaviour the moment it is finally shown.</param>
    public void Update(double delta, Vec2 pointerOverlay, bool visible)
    {
        if (!visible)
        {
            // Nothing to simulate while the character hasn't been released yet (or has been
            // hidden again) — otherwise it keeps walking/sitting/running on the invisible
            // desktop floor the whole time the launcher sits open, and shows up already deep
            // into some unrelated state the moment it's finally released.
            return;
        }

        if (!_isPaused)
        {
            _animationTime += delta;
            _stateTime += delta;
            _secondsSinceInteraction += delta;
            UpdateCrouchAmount(delta);
            UpdateClimbAmount(delta);
            UpdateHideAmount(delta);
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
                UpdateHiding(delta, snapshot);
            }
            else
            {
                UpdateGroundedPhysics(delta, snapshot);
            }

            RecoverFromInvalidPhysicsState();
            UpdateAutonomousBehavior();
            if (_clicked && _stateTime > 0.45)
            {
                _clicked = false;
            }
        }
    }

    /// <summary>Recovers from a renderer blowing up on this frame's data. Hosts call this
    /// when their draw call throws: whatever field went bad, the character is put back
    /// somewhere sane rather than crashing the paint loop on every subsequent frame forever
    /// — see <see cref="ResetPhysicsToSafeState"/>.</summary>
    public void NotifyRenderFailed(string error)
    {
        _logger.Write("character_render_failed", new { error });
        ResetPhysicsToSafeState("render_exception");
    }

    /// <summary>Every position/size-mutating path on <see cref="CharacterPhysics"/> other
    /// than <see cref="CharacterPhysics.SetPosition"/> (climbing, landing, hard-floor
    /// recovery, rescaling, ...) writes straight through without validating — a bad read
    /// somewhere upstream (e.g. degenerate window geometry mid-resize/close) can turn into a
    /// NaN or astronomically large position or size that never throws on its own, but GDI+
    /// does the moment it's asked to draw an ellipse at those coordinates — and it does so
    /// every single frame, with nothing to ever recover, since the underlying bad state just
    /// sits there. The overlay is borderless, click-through and not in the taskbar, so that
    /// reads as a permanently stuck, unclosable window (confirmed in production crash logs —
    /// twice: a `Graphics.DrawEllipse` "generic error" from <see cref="CharacterRenderer"/>
    /// repeating with no recovery in between. The first attempt at this guard only checked
    /// Position, not Size — it still happened again, which is why this now checks both and
    /// <see cref="OnPaint"/> additionally wraps the actual draw call as a last-resort net for
    /// whatever specific field turns out to be the next one). Catching it here, right before
    /// the paint that would otherwise crash, and snapping back to a safe on-screen spot is
    /// the same "one absolute boundary" recovery already used for falling past the floor.</summary>
    private void RecoverFromInvalidPhysicsState()
    {
        RectD virtualBounds = _screens.VirtualBounds;
        if (IsPositionSane(_physics.Position, virtualBounds) && IsSizeSane(_physics.Size))
        {
            return;
        }

        ResetPhysicsToSafeState("invalid_physics_state");
    }

    /// <summary>Shared by the proactive per-frame check above and by <see cref="OnPaint"/>'s
    /// catch-all around the actual draw call — the latter exists because no per-field check
    /// can promise to cover every future source of a bad value, only this can.</summary>
    private void ResetPhysicsToSafeState(string reason)
    {
        Vec2 badPosition = _physics.Position;
        Size2 badSize = _physics.Size;
        _wallClimb.Stop();
        _attachment.Detach();
        _currentPlatformId = null;
        _hidingPlatformId = null;
        double safeScale = double.IsFinite(_characterScale) ? _characterScale : 1.0;
        _physics.Rescale(new Size2(
            BaseCharacterSize.Width * safeScale,
            BaseCharacterSize.Height * safeScale));
        PositionAtPrimaryScreen();
        _stateMachine.Send(CharacterSignal.SupportLost);
        _logger.Write("character_position_recovered", new
        {
            reason,
            bad_position = badPosition.ToString(),
            bad_size = badSize.ToString(),
        });
    }

    private static bool IsPositionSane(Vec2 position, RectD virtualBounds)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            return false;
        }

        // Generous margin beyond the actual virtual screen — legitimate motion (a big
        // throw, climbing off toward a monitor edge) never needs more than a screen's worth
        // of slack, so anything beyond that is corrupted state, not a character that's
        // merely off-screen for a moment.
        double margin = Math.Max(virtualBounds.Width, virtualBounds.Height) + 5_000;
        return position.X >= virtualBounds.X - margin && position.X <= virtualBounds.Right + margin
            && position.Y >= virtualBounds.Y - margin && position.Y <= virtualBounds.Bottom + margin;
    }

    private static bool IsSizeSane(Size2 size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height) &&
        size.Width > 0 && size.Height > 0 && size.Width < 10_000 && size.Height < 10_000;

    private void UpdateGroundedPhysics(double delta, PlatformSnapshot snapshot)
    {
        DesktopPlatform? attachedPlatform = FollowAttachedPlatform(snapshot);
        (double left, double right, DesktopPlatform? leftWall, DesktopPlatform? rightWall) =
            GetHorizontalBoundaries(attachedPlatform, snapshot);
        CharacterMotionStep motion = _physics.Integrate(delta, _stateMachine.Current, left, right);

        // Read by UpdateFleeBehavior (one frame later — an imperceptible lag) to suppress
        // its own independent jump roll while the wall-encounter reaction below is already
        // deciding what to do about the exact same wall. Without this, a fleeing character
        // pressed against a wall it's about to climb could also roll a flee-jump on any of
        // the several frames it takes HandleWallEncounter's own roll to succeed, bouncing it
        // into a little hop-and-immediately-land loop right at the wall before climbing
        // actually starts.
        _pressedAgainstWall = motion.HitHorizontalEdge && (leftWall is not null || rightWall is not null);

        if (_stateMachine.Current == CharacterState.Fall)
        {
            if (_physics.Velocity.Y < 0)
            {
                if (!_jumpGrabAttempted)
                {
                    WallGrabDetector.Reach? reach = WallGrabDetector.FindReachableEdge(
                        motion.CurrentBounds,
                        _physics.Velocity.X,
                        WithScreenEdges(snapshot.Platforms),
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
            else
            {
                // ResolveDownward only catches a continuous downward sweep crossing a
                // surface — if the character is already past the desktop floor at the very
                // start of a fall (e.g. having climbed down past a window that extends
                // below the work area, or some other edge case placing it there directly),
                // the sweep never "crosses" it and gravity would otherwise pull it further
                // away every frame with nothing to ever catch it, off the bottom of the
                // screen for good. The desktop floor is the one absolute boundary, so
                // treat already being past it as an instant landing too.
                double floorY = CreateDesktopPlatform().Segments[0].SurfaceY;
                if (motion.CurrentBounds.Bottom >= floorY)
                {
                    _physics.LandOn(floorY);
                    _currentPlatformId = "desktop:work-area";
                    _attachment.Detach();
                    _logger.Write("character_hard_floor_recovery", new { floor_y = floorY });
                    _stateMachine.Send(CharacterSignal.Landed);
                }
            }
        }
        else
        {
            if (_stateMachine.Current is not (CharacterState.Idle or CharacterState.Walk
                or CharacterState.Run or CharacterState.Sit))
            {
                return;
            }

            // A window standing in the walking path reacts the same way whether the
            // character is perched on top of another window or on the bare desktop floor —
            // previously this whole block (and thus GetHorizontalBoundaries's neighbor-wall
            // search) only ran while attachedPlatform was a window, so a window sitting in
            // front of a character walking on the bare floor was silently walked straight
            // through with no reaction at all.
            if (motion.HitHorizontalEdge && _stateMachine.Current is CharacterState.Walk or CharacterState.Run)
            {
                // motion.HitEdgeDirection is the direction of travel that caused the hit,
                // captured before CharacterPhysics.Integrate's own bounce-turnaround flips
                // WalkDirection to the opposite sign — reading _physics.WalkDirection here
                // instead (as this used to) picks the wall/edge the character is NOT
                // touching, teleporting it across to the far side to grab it.
                DesktopPlatform? neighborWall = motion.HitEdgeDirection > 0 ? rightWall : leftWall;
                if (neighborWall is not null)
                {
                    HandleWallEncounter(neighborWall, WallSideResolver.ForEncounteredWall(motion.HitEdgeDirection));
                    return;
                }

                if (attachedPlatform is { Kind: PlatformKind.Window } &&
                    Random.Shared.NextDouble() < _behaviorTuning.ClimbChance)
                {
                    StartClimb(attachedPlatform, WallSideResolver.ForOwnEdge(motion.HitEdgeDirection));
                    return;
                }
            }

            if (attachedPlatform is null)
            {
                return;
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
        _wallClimb.Start(platform, side, _physics.Size, GetCurrentScreenBounds());
        _climbDirection = initialDirection;
        _climbPlatformMissingSeconds = 0;
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

    /// <summary>A window other than the one being walked on (or the bare edge of the
    /// screen itself) rises up across the walking line ahead — with some chance climb up
    /// its near face, with some chance duck out of sight behind it, otherwise it's just a
    /// wall and the bounce that already happened this frame (via the tightened boundary
    /// from <see cref="GetHorizontalBoundaries"/>) is the whole reaction.</summary>
    private void HandleWallEncounter(DesktopPlatform wall, WallSide side)
    {
        double roll = Random.Shared.NextDouble();
        if (roll < _behaviorTuning.ClimbChance)
        {
            StartClimb(wall, side, initialDirection: -1);
            return;
        }

        // Hiding "behind" the bare edge of the screen doesn't make sense — there's nothing
        // there to hide behind.
        if (wall.Kind != PlatformKind.ScreenEdge && roll < _behaviorTuning.ClimbChance + _behaviorTuning.HideChance)
        {
            EnterHide(wall, side);
        }
    }

    private void EnterHide(DesktopPlatform wall, WallSide side)
    {
        // X tucks the character beside the wall's edge; Y stays close to where it was
        // already standing — a little lower still (HideLowerOffset), not up at the wall's
        // own top corner — so the peek reads at roughly natural head height next to the
        // wall, the way a person actually crouched slightly to peek around a corner would.
        // Captured before the position snaps to the tucked spot below — OnPaint blends the
        // *rendered* position from here up to the real (already-updated) physics position
        // over HidePoseBlendSeconds, so the character visibly settles into the corner
        // instead of teleporting there on the first Hide frame.
        _hideEntryPosition = _physics.Position;

        double bodyX = side == WallSide.Left
            ? wall.Bounds.X - (_physics.Size.Width * 0.5)
            : wall.Bounds.Right - (_physics.Size.Width * 0.5);
        double bodyY = _physics.Position.Y + (_physics.Size.Height * HideLowerOffset);
        _physics.SetPosition(new Vec2(bodyX, bodyY));
        _hidingPlatformId = wall.Id;
        _hidingStartBounds = wall.Bounds;
        _hidingSide = side;
        _hideAmount = 0;
        _hidingPlatformMissingSeconds = 0;
        _attachment.Detach();
        _currentPlatformId = null;
        _stateMachine.Send(CharacterSignal.HideRequested);
        _logger.Write("character_hiding", new { wall = wall.Id, side = side.ToString() });
    }

    private void UpdateHiding(double delta, PlatformSnapshot snapshot)
    {
        DesktopPlatform? wall = snapshot.Platforms.FirstOrDefault(p => p.Id == _hidingPlatformId);
        if (wall is null)
        {
            _hidingPlatformMissingSeconds += delta;
            if (_hidingPlatformMissingSeconds < PlatformMissingGraceSeconds)
            {
                // The wall being hidden behind can vanish from a single snapshot when
                // something briefly covers it full-screen (e.g. the screenshot/snip flash,
                // or a reconciliation racing a toast notification) — that's not the window
                // actually closing, so ride out a short grace window in place instead of
                // dropping out of hiding over one bad frame.
                return;
            }

            _stateMachine.Send(CharacterSignal.SupportLost);
            return;
        }

        _hidingPlatformMissingSeconds = 0;
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
        DesktopPlatform? platform = ResolveClimbPlatform(snapshot);
        if (platform is null)
        {
            _climbPlatformMissingSeconds += delta;
            if (_climbPlatformMissingSeconds < PlatformMissingGraceSeconds)
            {
                // The climbed window can vanish from a single snapshot when something
                // briefly covers it full-screen (e.g. the screenshot/snip flash animation,
                // or a reconciliation racing a toast notification) — that's not the window
                // actually closing, so ride out a short grace window holding position
                // instead of dropping the climb over one bad frame.
                return;
            }

            _wallClimb.Stop();
            _stateMachine.Send(CharacterSignal.SupportLost);
            return;
        }

        _climbPlatformMissingSeconds = 0;

        // Re-sync every frame, not just once at Start(): otherwise a window dragged or
        // resized mid-climb leaves the character clinging to wherever it used to be. The
        // screen bounds are passed every time too, since the character can drift toward a
        // different monitor mid-climb (e.g. a push-off) and the clamp should always follow
        // whichever monitor it's actually next to.
        _wallClimb.Retarget(platform, _physics.Size, GetCurrentScreenBounds());

        double roll = Random.Shared.NextDouble();
        if (roll < ClimbLetGoChancePerSecond * delta)
        {
            // Just loses its grip and drops — a climb doesn't only ever end at the top or
            // bottom of the wall.
            _wallClimb.Stop();
            _stateMachine.Send(CharacterSignal.SupportLost);
            return;
        }

        if (roll < (ClimbLetGoChancePerSecond + ClimbPushOffChancePerSecond) * delta)
        {
            // Pushes off sideways, away from the wall, with enough of an upward kick to
            // plausibly land on top of something nearby instead of just dropping straight
            // down — the same Fall-state landing/ceiling/jump-grab logic already handles
            // wherever it ends up.
            double pushDirection = _wallClimb.Side == WallSide.Left ? -1 : 1;
            _wallClimb.Stop();
            _stateMachine.Send(CharacterSignal.SupportLost);
            _physics.Nudge(new Vec2(pushDirection * ClimbPushOffHorizontalSpeed, -ClimbPushOffVerticalImpulse));
            _logger.Write("character_pushed_off_wall", new { platform = platform.Id });
            return;
        }

        if (Random.Shared.NextDouble() < ClimbReverseChancePerSecond * delta)
        {
            _climbDirection = -_climbDirection;
        }

        ClimbStep step = _wallClimb.Advance(_physics.Position.Y, _climbDirection * ClimbSpeed * delta);
        _physics.SetPosition(step.Position);

        if (step.Outcome == ClimbOutcome.ReachedTop)
        {
            if (platform.Kind == PlatformKind.ScreenEdge)
            {
                // The side of the screen has no horizontal surface to climb out onto at the
                // top, unlike a window — just runs out of wall and drops.
                _wallClimb.Stop();
                _stateMachine.Send(CharacterSignal.SupportLost);
                return;
            }

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

    /// <summary>Looks up the platform a climb is currently clinging to. Real windows come
    /// straight from the snapshot; the synthetic screen-edge walls are never part of it (the
    /// window platform provider has no reason to know about them), so they're rebuilt fresh
    /// from the current monitor geometry instead — that also keeps them correct if the work
    /// area itself changes mid-climb (e.g. the taskbar auto-hiding).</summary>
    private DesktopPlatform? ResolveClimbPlatform(PlatformSnapshot snapshot)
    {
        if (_wallClimb.PlatformId == LeftScreenEdgeId || _wallClimb.PlatformId == RightScreenEdgeId)
        {
            return CreateScreenEdgePlatform(isLeftEdge: _wallClimb.PlatformId == LeftScreenEdgeId);
        }

        return snapshot.Platforms.FirstOrDefault(p => p.Id == _wallClimb.PlatformId);
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
            case CharacterState.Hide when _stateTime >= _behaviorTuning.HideDuration:
                // Otherwise hiding has no way out at all short of the wall itself moving or
                // the user grabbing it — routing through SupportLost (like the "wall moved"
                // exit already does) re-derives gravity/landing normally next frame instead
                // of assuming Idle is safe: if the wall is still right there it re-lands
                // within a frame or two (imperceptible), and if it isn't, the character
                // properly falls instead of floating in place.
                _hidingPlatformId = null;
                _stateMachine.Send(CharacterSignal.SupportLost);
                _logger.Write("character_stopped_hiding");
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
            // Pressed against a wall, HandleWallEncounter is already rolling its own
            // climb/hide/stop reaction every frame — an independent jump attempt on top of
            // that just produces a little hop-and-immediately-land right at the wall,
            // possibly several times in a row before the wall reaction finally resolves.
            if (!_pressedAgainstWall && Random.Shared.NextDouble() < FleeJumpChancePerSecond * delta)
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
        if (_stateMachine.Current is CharacterState.Walk or CharacterState.Run)
        {
            // Once the character is actively walking/running again (e.g. it lands off a
            // climb and immediately resumes fleeing), the stride animation takes over the
            // arms on its own — continuing the usual ClimbPoseBlendSeconds fade-out at the
            // same time would have the arms blending toward a now-stale climbWallLineX
            // while the stride's own oscillation is simultaneously swinging them for
            // walking, the two fighting over the same arm position for the whole fade and
            // reading as the arms twitching/pawing at the air where the wall used to be.
            // Idle keeps the graceful fade (nothing else is competing for the arms there).
            _climbAmount = 0;
            return;
        }

        double target = _stateMachine.Current == CharacterState.Climb ? 1 : 0;
        double step = delta / ClimbPoseBlendSeconds;
        _climbAmount = target > _climbAmount
            ? Math.Min(target, _climbAmount + step)
            : Math.Max(target, _climbAmount - step);
    }

    /// <summary>Eases the hide pose's rotation and slide-in position toward 1 over <see
    /// cref="HidePoseBlendSeconds"/> instead of popping into the rotated peek stance and
    /// final tucked position on a single frame.</summary>
    private void UpdateHideAmount(double delta)
    {
        double target = _stateMachine.Current == CharacterState.Hide ? 1 : 0;
        double step = delta / HidePoseBlendSeconds;
        _hideAmount = target > _hideAmount
            ? Math.Min(target, _hideAmount + step)
            : Math.Max(target, _hideAmount - step);
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

                // A different window standing in the way is a wall too, and must stop
                // horizontal motion at its own edge even if the walked-on platform's
                // (occlusion-clipped) surface segment would technically continue past it.
                WallEncounterDetector.Neighbors neighbors = WallEncounterDetector.FindNeighborWalls(
                    attachedPlatform,
                    _physics.Bounds,
                    snapshot.Platforms);
                return WithScreenEdgeFallback(
                    ClampToNeighborWalls(segment.Value.Left - overhang, segment.Value.Right + overhang, neighbors));
            }
        }

        // No attached window means the character is walking the bare desktop floor — but a
        // window can still be sitting squarely in its path there, and must stop it exactly
        // like a neighbor window would while perched on top of a different platform.
        (_, double workLeft, double workRight) = GetCurrentWorkArea();
        WallEncounterDetector.Neighbors floorNeighbors = WallEncounterDetector.FindNeighborWalls(
            CreateDesktopPlatform(),
            _physics.Bounds,
            snapshot.Platforms);
        return WithScreenEdgeFallback(ClampToNeighborWalls(workLeft, workRight, floorNeighbors));
    }

    /// <summary>When neither a real platform edge nor a neighbor window constrains a side,
    /// the boundary is the literal edge of the monitor's work area — substitute the
    /// synthetic screen-edge wall there so walking into it offers the same climb/bounce
    /// reaction as walking into a window, instead of just silently stopping.</summary>
    private (double Left, double Right, DesktopPlatform? LeftWall, DesktopPlatform? RightWall) WithScreenEdgeFallback(
        (double Left, double Right, DesktopPlatform? LeftWall, DesktopPlatform? RightWall) bounds)
    {
        (_, double workLeft, double workRight) = GetCurrentWorkArea();
        DesktopPlatform? leftWall = bounds.LeftWall;
        DesktopPlatform? rightWall = bounds.RightWall;
        double left = bounds.Left;
        double right = bounds.Right;
        if (leftWall is null && left <= workLeft + 0.5)
        {
            leftWall = CreateScreenEdgePlatform(isLeftEdge: true);
        }

        if (rightWall is null && right >= workRight - 0.5)
        {
            rightWall = CreateScreenEdgePlatform(isLeftEdge: false);
        }

        // A window's own surface can extend past the monitor's work area (dragged so it
        // hangs off the side) — a segment sitting on top of it must still stop walking at
        // the real screen edge instead of continuing onto the off-screen portion, or the
        // character can walk clean off the visible desktop and only snap back once
        // something else re-clamps it (reading as a sudden teleport).
        if (left < workLeft)
        {
            left = workLeft;
        }

        if (right > workRight)
        {
            right = workRight;
        }

        return (left, right, leftWall, rightWall);
    }

    private RectD GetCurrentScreenBounds()
    {
        (double floor, double left, double right) = GetCurrentWorkArea();
        double top = GetCurrentMonitorTop();
        return new RectD(left, top, right - left, floor - top);
    }

    private DesktopPlatform CreateScreenEdgePlatform(bool isLeftEdge)
    {
        (double floor, double workLeft, double workRight) = GetCurrentWorkArea();
        double top = GetCurrentMonitorTop();
        const double thickness = 2;
        double edgeX = isLeftEdge ? workLeft : workRight;
        double left = isLeftEdge ? edgeX - thickness : edgeX;
        return new DesktopPlatform
        {
            Id = isLeftEdge ? LeftScreenEdgeId : RightScreenEdgeId,
            Kind = PlatformKind.ScreenEdge,
            Bounds = new RectD(left, top, thickness, floor - top),
            Segments = [new PlatformSegment(left, left + thickness, top)],
            ZOrder = int.MaxValue,
            MonitorId = "screen-edge",
            MonitorTop = top,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private List<DesktopPlatform> WithScreenEdges(ImmutableArray<DesktopPlatform> platforms)
    {
        var combined = new List<DesktopPlatform>(platforms.Length + 2);
        combined.AddRange(platforms);
        combined.Add(CreateScreenEdgePlatform(isLeftEdge: true));
        combined.Add(CreateScreenEdgePlatform(isLeftEdge: false));
        return combined;
    }

    private static (double Left, double Right, DesktopPlatform? LeftWall, DesktopPlatform? RightWall) ClampToNeighborWalls(
        double left,
        double right,
        WallEncounterDetector.Neighbors neighbors)
    {
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

    private (double Floor, double Left, double Right) GetCurrentWorkArea()
    {
        RectD workArea = _screens.WorkAreaAt(CharacterCenter());
        return (workArea.Bottom, workArea.X, workArea.Right);
    }

    private double GetCurrentMonitorTop() => _screens.MonitorTopAt(CharacterCenter());

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

    private Vec2 CharacterCenter() => new(
        _physics.Position.X + (_physics.Size.Width / 2),
        _physics.Position.Y + (_physics.Size.Height / 2));

    private void PositionAtPrimaryScreen()
    {
        RectD work = _screens.PrimaryWorkArea;
        var start = new Vec2(
            work.X + ((work.Width - _physics.Size.Width) / 2),
            work.Y + 30);
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
}
