using System.ComponentModel;
using System.Diagnostics;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.App;

internal sealed class OverlayForm : Form
{
    private static readonly Color TransparencyColor = Color.Fuchsia;
    private readonly CharacterStateMachine _stateMachine = new();
    private readonly CharacterPhysics _physics;
    private readonly CharacterRenderer _renderer = new();
    private readonly IWindowPlatformProvider _windowPlatforms;
    private readonly PlatformCollisionResolver _collisionResolver = new();
    private readonly CharacterPlatformAttachment _attachment = new();
    private readonly CharacterPlatformController _platformController;
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

    public OverlayForm(
        JsonLineLogger logger,
        int targetFps,
        IWindowPlatformProvider windowPlatforms)
    {
        _logger = logger;
        _windowPlatforms = windowPlatforms;
        _physics = new CharacterPhysics(new Vec2(120, 60), new Size2(94, 178));
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
        PositionAtPrimaryScreen();
        (RectD overlayBounds, RectD virtualBounds) = GetCoordinateSpace();
        _windowPlatforms.Start(overlayBounds, virtualBounds);
        _logger.Write("overlay_created", new
        {
            virtual_screen = SystemInformation.VirtualScreen.ToString(),
            monitor_count = Screen.AllScreens.Length,
        });
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
        _renderer.Draw(e.Graphics, _physics.Bounds, _stateMachine.Current, _animationTime, _clicked);

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
        if (e.Button != MouseButtons.Left || !_physics.Bounds.Inflate(8, 8).Contains(pointer))
        {
            return;
        }

        _isHolding = true;
        _attachment.Detach();
        _currentPlatformId = null;
        Capture = true;
        _mouseDown = pointer;
        _previousPointer = pointer;
        _previousPointerTicks = _clock.ElapsedTicks;
        _releaseVelocity = Vec2.Zero;
        _lastMouseEvent = "grab";
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

        if (!_isPaused)
        {
            _animationTime += delta;
            _stateTime += delta;

            if (_stateMachine.Current == CharacterState.Spawn)
            {
                _stateMachine.Send(CharacterSignal.Tick);
            }

            PlatformSnapshot snapshot = _windowPlatforms.Snapshot;
            DesktopPlatform? attachedPlatform = FollowAttachedPlatform(snapshot);
            (double left, double right) = GetHorizontalBoundaries(attachedPlatform);
            CharacterMotionStep motion = _physics.Integrate(
                delta,
                _stateMachine.Current,
                left,
                right);
            _previousBottom = motion.PreviousBounds.Bottom;
            _currentBottom = motion.CurrentBounds.Bottom;

            if (_stateMachine.Current == CharacterState.Fall)
            {
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
                     _stateMachine.Current is CharacterState.Idle or CharacterState.Walk)
            {
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

            UpdateAutonomousBehavior();
            if (_clicked && _stateTime > 0.45)
            {
                _clicked = false;
            }
        }

        UpdateClickThrough();
        Invalidate();
    }

    private void UpdateAutonomousBehavior()
    {
        double idleDelay = 2.4;
        double walkDuration = 3.8;
        if (_stateMachine.Current == CharacterState.Idle && _stateTime >= idleDelay)
        {
            _stateMachine.Send(CharacterSignal.WalkRequested);
        }
        else if (_stateMachine.Current == CharacterState.Walk && _stateTime >= walkDuration)
        {
            _stateMachine.Send(CharacterSignal.StopRequested);
        }
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

    private (double Left, double Right) GetHorizontalBoundaries(DesktopPlatform? attachedPlatform)
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
                return (segment.Value.Left - overhang, segment.Value.Right + overhang);
            }
        }

        (_, double left, double right) = GetCurrentWorkArea();
        return (left, right);
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
        _attachment.Detach();
        _currentPlatformId = null;
        _stateMachine.Send(CharacterSignal.SupportLost);
        _logger.Write("character_platform_lost", new { platform = lostPlatform });
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

    private void UpdateClickThrough()
    {
        if (!IsHandleCreated || !Visible)
        {
            return;
        }

        Point clientPointer = PointToClient(Cursor.Position);
        bool overCharacter = _physics.Bounds.Inflate(8, 8).Contains(
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
        const int width = 250;
        const int height = 190;
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
            $"Clip: {_stateMachine.Current.ToString().ToLowerInvariant()}";
        graphics.DrawString(details, Font, text, new RectangleF(26, 24, width - 20, height - 16));

        using var hitbox = new Pen(Color.LimeGreen, 1);
        RectD bounds = _physics.Bounds;
        graphics.DrawRectangle(
            hitbox,
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height);
    }
#endif
}
