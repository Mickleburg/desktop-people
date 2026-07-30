using System.ComponentModel;
using System.Diagnostics;
using DesktopPeople.Core;

namespace DesktopPeople.App;

internal sealed class OverlayForm : Form
{
    private static readonly Color TransparencyColor = Color.Fuchsia;
    private readonly CharacterStateMachine _stateMachine = new();
    private readonly CharacterPhysics _physics;
    private readonly CharacterRenderer _renderer = new();
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

    public OverlayForm(JsonLineLogger logger, int targetFps)
    {
        _logger = logger;
        _physics = new CharacterPhysics(new Vec2(120, 60), new Size2(94, 178));

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
        _logger.Write("overlay_created", new
        {
            virtual_screen = SystemInformation.VirtualScreen.ToString(),
            monitor_count = Screen.AllScreens.Length,
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _renderer.Draw(e.Graphics, _physics.Bounds, _stateMachine.Current, _animationTime, _clicked);

#if DEBUG
        DrawDebugPanel(e.Graphics);
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

        if (!_isPaused)
        {
            _animationTime += delta;
            _stateTime += delta;

            if (_stateMachine.Current == CharacterState.Spawn)
            {
                _stateMachine.Send(CharacterSignal.Tick);
            }

            (double floor, double left, double right) = GetCurrentWorkArea();
            PhysicsStepResult result = _physics.Step(
                delta,
                _stateMachine.Current,
                floor,
                left,
                right);

            if (result.Landed)
            {
                _stateMachine.Send(CharacterSignal.Landed);
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
        Point characterCenter = new(
            Bounds.Left + (int)(_physics.Position.X + (_physics.Size.Width / 2)),
            Bounds.Top + (int)(_physics.Position.Y + (_physics.Size.Height / 2)));
        Rectangle workArea = Screen.FromPoint(characterCenter).WorkingArea;
        return (
            workArea.Bottom - Bounds.Top,
            workArea.Left - Bounds.Left,
            workArea.Right - Bounds.Left);
    }

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
    private void DrawDebugPanel(Graphics graphics)
    {
        const int width = 250;
        const int height = 144;
        using var background = new SolidBrush(Color.FromArgb(220, 27, 29, 40));
        using var text = new SolidBrush(Color.White);
        graphics.FillRectangle(background, 14, 14, width, height);

        string details =
            $"DesktopPeople • DEBUG\n" +
            $"FPS: {_fps:F0}\n" +
            $"State: {_stateMachine.Current}\n" +
            $"Velocity: {_physics.Velocity.X:F0}, {_physics.Velocity.Y:F0}\n" +
            $"Platform: desktop work area\n" +
            $"Windows: 0 (этап 2)\n" +
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
