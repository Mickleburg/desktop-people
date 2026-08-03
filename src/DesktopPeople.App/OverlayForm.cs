using System.ComponentModel;
using System.Diagnostics;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.App;

/// <summary>
/// The WinForms host: a transparent, click-through, always-on-top window that owns painting
/// and input, and nothing else. All behaviour — physics, climbing, hiding, fleeing, the
/// recovery guards — lives in <see cref="CharacterSimulation"/> so it is shared with the
/// Godot host and, unlike when it lived here, actually reachable by unit tests.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const double TopMostReassertIntervalSeconds = 2.0;
    private static readonly Color TransparencyColor = Color.Fuchsia;

    private readonly CharacterSimulation _simulation;
    private readonly IWindowPlatformProvider _windowPlatforms;
    private readonly CharacterRenderer _renderer = new();
    private readonly JsonLineLogger _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _previousTicks;
    private double _topMostReassertSeconds;
    private bool _clickThrough;
    private bool _showPlatformDebug;
    private string _lastMouseEvent = "none";
    private double _fps;
    private int _frames;
    private double _fpsElapsed;

    public OverlayForm(
        JsonLineLogger logger,
        int targetFps,
        IWindowPlatformProvider windowPlatforms)
    {
        _logger = logger;
        _windowPlatforms = windowPlatforms;
        _simulation = new CharacterSimulation(
            logger,
            windowPlatforms,
            new WinFormsScreenGeometry(() => Bounds));

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
        get => _simulation.IsPaused;
        set
        {
            _simulation.IsPaused = value;
            _lastMouseEvent = value ? "paused" : "resumed";
            Invalidate();
        }
    }

    public CharacterState State => _simulation.State;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string BehaviorIntensity
    {
        get => _simulation.BehaviorIntensity;
        set => _simulation.BehaviorIntensity = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double CharacterScale
    {
        get => _simulation.CharacterScale;
        set => _simulation.CharacterScale = value;
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
        (RectD overlayBounds, RectD virtualBounds) = GetCoordinateSpace();
        _simulation.Start(overlayBounds, virtualBounds);
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
        CharacterFrame frame = _simulation.CurrentFrame();

        // The overlay is a topmost click-through layer, so there is no real Z-order to hide
        // behind — the only way the character can read as being behind a window is to not
        // paint the part that would be covered.
        Region? previousClip = null;
        if (frame.HidingWallBounds is { } wall)
        {
            previousClip = e.Graphics.Clip;
            using Region clipped = previousClip.Clone();
            clipped.Exclude(new RectangleF(
                (float)wall.X,
                (float)wall.Y,
                (float)wall.Width,
                (float)wall.Height));
            e.Graphics.Clip = clipped;
        }

        try
        {
            _renderer.Draw(
                e.Graphics,
                frame.Body,
                new CharacterPose(
                    frame.State,
                    frame.AnimationTime,
                    frame.Clicked,
                    frame.CrouchAmount,
                    frame.GazeTarget,
                    frame.ClimbWallDirection,
                    ShowShadow: _showPlatformDebug,
                    ClimbAmount: frame.ClimbAmount,
                    HidePeekDirection: frame.HidePeekDirection,
                    HideAmount: frame.HideAmount));
        }
        catch (Exception ex)
        {
            // Last-resort net. The simulation's own per-frame sanity check only covers the
            // fields known to have gone bad before (position, then size — two rounds, two
            // fields), and nothing upstream can promise to cover the next one. Catching here,
            // at the boundary where the crash actually happens, is what stops a single bad
            // float from crashing this paint on every frame forever and leaving a borderless,
            // taskbar-less window nobody can close.
            _simulation.NotifyRenderFailed(ex.ToString());
        }

        if (previousClip is not null)
        {
            e.Graphics.Clip = previousClip;
            previousClip.Dispose();
        }

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
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_simulation.TryGrab(new Vec2(e.X, e.Y), _clock.Elapsed.TotalSeconds))
        {
            Capture = true;
            _lastMouseEvent = "grab";
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_simulation.IsHeld)
        {
            return;
        }

        _simulation.Drag(new Vec2(e.X, e.Y), _clock.Elapsed.TotalSeconds);
        _lastMouseEvent = "drag";
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_simulation.IsHeld || e.Button != MouseButtons.Left)
        {
            return;
        }

        _simulation.ReleaseGrab(new Vec2(e.X, e.Y));
        Capture = false;
        _lastMouseEvent = "release";
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
            _previousTicks = 0;
            _simulation.Update(0, Vec2.Zero, visible: false);
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
        ReassertTopMostPeriodically(delta);

        // Pumping the platform provider stays here rather than inside the simulation: it
        // needs this window's own screen-space rectangle to map real windows into overlay
        // coordinates, which only the host knows.
        (RectD overlayBounds, RectD virtualBounds) = GetCoordinateSpace();
        _windowPlatforms.Pump(DateTimeOffset.UtcNow, overlayBounds, virtualBounds);

        Point clientPointer = IsHandleCreated ? PointToClient(Cursor.Position) : Point.Empty;
        _simulation.Update(delta, new Vec2(clientPointer.X, clientPointer.Y), visible: true);

        UpdateClickThrough(clientPointer);
        Invalidate();
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

        bool overCharacter = _simulation.HitTest(new Vec2(clientPointer.X, clientPointer.Y));
        bool shouldClickThrough = !_simulation.IsHeld && !overCharacter;
        if (shouldClickThrough == _clickThrough)
        {
            return;
        }

        _clickThrough = shouldClickThrough;
        NativeWindowStyles.SetClickThrough(Handle, shouldClickThrough);
    }

    /// <summary>`TopMost = true` (set once, at construction) only reflects WinForms' own
    /// cached intent — it doesn't mean the window is pinned there forever. Another
    /// application asserting its own topmost status can push this window back in the real
    /// Win32 z-order without WinForms ever finding out, so the character keeps simulating
    /// fine but silently stops being drawn on top of anything — reported by the user as "the
    /// character just vanished while running around", fixed only by toggling the tray
    /// visibility item (which happens to call BringToFront). Re-asserting HWND_TOPMOST
    /// directly on an interval, regardless of what the cached property claims, is the actual
    /// fix instead of relying on the user noticing and toggling it back manually.</summary>
    private void ReassertTopMostPeriodically(double delta)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        _topMostReassertSeconds += delta;
        if (_topMostReassertSeconds < TopMostReassertIntervalSeconds)
        {
            return;
        }

        _topMostReassertSeconds = 0;
        NativeWindowStyles.ForceTopMost(Handle);
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

        CharacterDiagnostics diagnostics = _simulation.Diagnostics();
        using var footPen = new Pen(Color.LimeGreen, 4);
        graphics.DrawLine(
            footPen,
            (float)diagnostics.FootInterval.Left,
            (float)diagnostics.Body.Bottom,
            (float)diagnostics.FootInterval.Right,
            (float)diagnostics.Body.Bottom);

        if (diagnostics.AttachmentFootCenterX is { } attachmentX)
        {
            using var attachmentBrush = new SolidBrush(Color.HotPink);
            graphics.FillEllipse(
                attachmentBrush,
                (float)attachmentX - 5,
                (float)diagnostics.Body.Bottom - 5,
                10,
                10);
        }
    }

    private void DrawDebugPanel(Graphics graphics)
    {
        const int width = 260;
        const int height = 280;
        using var background = new SolidBrush(Color.FromArgb(220, 27, 29, 40));
        using var text = new SolidBrush(Color.White);
        graphics.FillRectangle(background, 14, 14, width, height);

        CharacterDiagnostics diagnostics = _simulation.Diagnostics();
        string details =
            $"DesktopPeople • DEBUG\n" +
            $"FPS: {_fps:F0}\n" +
            $"State: {diagnostics.State}\n" +
            $"Velocity: {diagnostics.Velocity.X:F0}, {diagnostics.Velocity.Y:F0}\n" +
            $"Platform: {diagnostics.CurrentPlatformId ?? "none"}\n" +
            $"Windows: {diagnostics.PlatformCount}\n" +
            $"Attached: {diagnostics.IsAttached}\n" +
            $"Mouse: {_lastMouseEvent}\n" +
            $"Intensity: {diagnostics.BehaviorIntensity}  Energy: {diagnostics.CursorEnergy:F2}\n" +
            $"Harassment: {diagnostics.HarassmentLevel:F1}  Fleeing: {diagnostics.IsFleeing}\n" +
            $"Climbing: {diagnostics.IsClimbing}  Hiding: {diagnostics.HidingPlatformId ?? "-"}\n" +
            $"Scale: {diagnostics.CharacterScale:F2}";
        graphics.DrawString(details, Font, text, new RectangleF(26, 24, width - 20, height - 16));

        using var hitbox = new Pen(Color.LimeGreen, 1);
        RectD interactive = _simulation.InteractiveBounds;
        graphics.DrawRectangle(
            hitbox,
            (float)interactive.X,
            (float)interactive.Y,
            (float)interactive.Width,
            (float)interactive.Height);
    }
#endif
}

/// <summary>Maps WinForms' screen APIs into the overlay-relative coordinates the simulation
/// works in. Calling <c>Screen.FromPoint</c> from the middle of the physics loop is exactly
/// what used to make that logic impossible to test outside a live window.</summary>
internal sealed class WinFormsScreenGeometry(Func<Rectangle> overlayBounds) : IScreenGeometry
{
    public RectD VirtualBounds
    {
        get
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Rectangle overlay = overlayBounds();
            return new RectD(
                virtualScreen.X - overlay.Left,
                virtualScreen.Y - overlay.Top,
                virtualScreen.Width,
                virtualScreen.Height);
        }
    }

    public int MonitorCount => Screen.AllScreens.Length;

    public RectD WorkAreaAt(Vec2 overlayPoint) =>
        ToOverlay(Screen.FromPoint(ToScreen(overlayPoint)).WorkingArea);

    public double MonitorTopAt(Vec2 overlayPoint) =>
        Screen.FromPoint(ToScreen(overlayPoint)).Bounds.Top - overlayBounds().Top;

    public RectD PrimaryWorkArea =>
        ToOverlay(Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea);

    private Point ToScreen(Vec2 overlayPoint)
    {
        Rectangle overlay = overlayBounds();
        return new Point(
            overlay.Left + (int)overlayPoint.X,
            overlay.Top + (int)overlayPoint.Y);
    }

    private RectD ToOverlay(Rectangle screenRect)
    {
        Rectangle overlay = overlayBounds();
        return new RectD(
            screenRect.Left - overlay.Left,
            screenRect.Top - overlay.Top,
            screenRect.Width,
            screenRect.Height);
    }
}
