using System.Runtime.InteropServices;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;
using DesktopPeople.Windows;
using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// The Godot host: window, input and drawing, and nothing else. Behaviour comes from the same
/// <see cref="CharacterSimulation"/> the WinForms host runs, and window enumeration from the
/// same <see cref="WindowsWindowPlatformProvider"/> — neither was modified for this port.
/// </summary>
public sealed partial class OverlayNode : Control
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly nint HwndTopMost = new(-1);
    private const double TopMostReassertIntervalSeconds = 2.0;
    private const double StalledFrameSeconds = 0.5;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;

    private readonly GodotCharacterRenderer _renderer = new();
    private DebugOverlay? _debugOverlay;
    private CharacterSimulation? _simulation;
    private WindowsWindowPlatformProvider? _windowPlatforms;
    private TrayIcon? _tray;
    private LauncherWindow? _launcher;
    private long _launcherHandle;
    private SettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private IOverlayLogger _logger = new GodotLogger();
    private bool _characterVisible = true;
    private Vector2I _overlayOrigin;
    private nint _handle;
    private double _topMostReassertSeconds;
    private double _elapsedSeconds;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        nint window, uint colorKey, byte alpha, uint flags);

    public override void _Ready()
    {
        Window window = GetTree().Root;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Window geometry is settled BEFORE transparency is switched on. Resizing or moving
        // an already-transparent window tears down its per-pixel composition on Windows and
        // does not rebuild it: the window stays see-through but nothing drawn into it is ever
        // presented — no error, no black screen, simply an invisible character.
        Rect2I desktop = GodotScreenGeometry.VirtualDesktop();
        _overlayOrigin = desktop.Position;
        window.Position = desktop.Position;

        // One pixel short of the full desktop on purpose: a borderless window at exactly the
        // native resolution is promoted to fullscreen-exclusive by Windows, and fullscreen
        // drops per-pixel transparency to opaque black.
        window.Size = desktop.Size - new Vector2I(0, 1);

        // TransparentBg has to be set on the root window itself (Window derives from
        // Viewport); going through GetViewport() does not resolve back to this viewport and
        // the clear colour stays opaque. Project settings alone are not enough either.
        window.TransparentBg = true;
        window.Transparent = true;
        window.Borderless = true;
        window.AlwaysOnTop = true;

        _handle = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        ApplyOverlayWindowStyles();

        _logger = CreateLogger();

        var registry = new PlatformRegistry();
        _windowPlatforms = new WindowsWindowPlatformProvider(
            new Win32WindowApi(),
            new Win32WindowEventSource(),
            registry,
            System.Environment.ProcessId);
        _windowPlatforms.SetExplicitlyExcludedHandles([_handle.ToInt64()]);

        _simulation = new CharacterSimulation(
            _logger,
            _windowPlatforms,
            new GodotScreenGeometry(() => _overlayOrigin));

        RectD overlayBounds = OverlayScreenBounds();
        _simulation.Start(overlayBounds, VirtualScreenBounds());
        LoadSettings();
        CreateDebugOverlay();
        CreateLauncher();
        _tray = new TrayIcon(
            _characterVisible,
            _simulation.IsPaused,
            _simulation.BehaviorIntensity,
            _settings.ShowPlatformDebug);
        _logger.Write("tray_created", new { status = _tray.Status });

        // Kept because its absence once cost a wrong conclusion: without the OS's own idea of
        // this window's rectangle beside Godot's, a DPI-scaling bug looked like a physics one.
        GetWindowRect(_handle, out Rect rect);
        _logger.Write("host_started", new
        {
            godot_size = window.Size.ToString(),
            os_rect = $"{rect.Left},{rect.Top}..{rect.Right},{rect.Bottom}",
            screen = DisplayServer.ScreenGetSize().ToString(),
            usable = DisplayServer.ScreenGetUsableRect().ToString(),
        });
    }

    /// <summary>Builds the developer view and leaves it in whatever state it was last left in.
    /// <para>
    /// Unlike the WinForms host this is not behind <c>#if DEBUG</c>. That host was only ever run
    /// from a debug build during development, while this one is now used as the exported
    /// artifact — and the mid-air hang showed what it costs to have no live view of the
    /// simulation when something goes wrong on the user's own desktop. It stays off unless the
    /// tray item is ticked, so shipping it costs nothing until it is asked for.
    /// </para>
    /// </summary>
    private void CreateDebugOverlay()
    {
        _debugOverlay = new DebugOverlay();
        AddChild(_debugOverlay);
        _debugOverlay.Attach(_simulation!, _windowPlatforms!);
        _debugOverlay.Visible = _settings.ShowPlatformDebug;
    }

    /// <summary>The character is deliberately not on the desktop yet: as in the WinForms host,
    /// it appears only once the user asks for it here. Starting the application should not put
    /// something on someone's screen before they have said go.</summary>
    private void CreateLauncher()
    {
        _characterVisible = false;
        _launcher = new LauncherWindow();
        AddChild(_launcher);

        _launcher.SelectedIntensity = _settings.BehaviorIntensity;
        _launcher.SelectedScalePercent = (int)Math.Round(_settings.CharacterScale * 100);
        _launcher.ReleaseRequested += () =>
        {
            _characterVisible = true;
            Save(_settings with { CharactersVisible = true });
            _tray?.SyncState(true, _simulation!.IsPaused, _simulation.BehaviorIntensity);
            _launcher.Hide();
            _logger.Write("character_released");
        };

        _launcher.IntensityChanged += intensity =>
        {
            SetIntensity(intensity);
            _tray?.SyncState(_characterVisible, _simulation!.IsPaused, intensity);
        };

        _launcher.ScaleChanged += percent =>
        {
            double scale = percent / 100.0;
            _simulation!.CharacterScale = scale;
            Save(_settings with { CharacterScale = scale });
        };

        ShowLauncher();
    }

    private void ShowLauncher()
    {
        if (_launcher is null)
        {
            return;
        }

        // Centred only the first time. Reopening a window the user has since moved should put
        // it back where they left it, like any other application window.
        bool firstTime = _launcherHandle == 0;
        _launcher.Show();
        if (firstTime)
        {
            _launcher.MoveToCenter();
        }

        _launcher.GrabFocus();
        TrackLauncherWindow();

        // Recorded because an embedded launcher is a silent failure: it renders inside the
        // transparent click-through overlay instead of being its own window, and then behaves
        // like neither.
        _logger.Write("launcher_shown", new
        {
            embedded = _launcher.IsEmbedded(),
            handle = _launcherHandle,
        });
    }

    /// <summary>Names the launch window explicitly among the handles the character ignores. The
    /// platform provider already skips every window of this process, so today this changes
    /// nothing — it is here so that the launcher stays excluded on its own merits if that
    /// process-wide rule is ever relaxed. The handle is re-read rather than captured once
    /// because Godot creates and destroys the OS window as it is shown and hidden.</summary>
    private void TrackLauncherWindow()
    {
        if (_launcher is null || _windowPlatforms is null || !_launcher.Visible)
        {
            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _launcher.GetWindowId());
        if (handle == _launcherHandle)
        {
            return;
        }

        _launcherHandle = handle;
        _windowPlatforms.SetExplicitlyExcludedHandles([_handle.ToInt64(), handle]);
    }

    /// <summary>Logs to Godot's console and to the same folder the WinForms host uses, under its
    /// own file name so the two can run at once without fighting over one file.</summary>
    private static IOverlayLogger CreateLogger()
    {
        try
        {
            return new GodotLogger(new JsonLineLogger(Path.Combine(DataDirectory(), "logs"), "godot"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Being unable to create the log folder is a reason to log less, not to refuse to
            // start: the character is the point, the diagnostics are not.
            var console = new GodotLogger();
            console.Write("file_logging_unavailable", new { error = exception.Message });
            return console;
        }
    }

    private static string DataDirectory() => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "DesktopPeople");

    public override void _Process(double delta)
    {
        if (_simulation is null || _windowPlatforms is null)
        {
            return;
        }

        // A frame this long means the whole host stopped for that time, which on screen is
        // indistinguishable from the character freezing mid-air. Recorded because the two have
        // completely different causes and the log is the only way to tell them apart after the
        // fact: a stall shows up here, a simulation that stopped landing shows up as
        // character_airborne_too_long instead.
        if (delta > StalledFrameSeconds)
        {
            _logger.Write("frame_stall", new { seconds = Math.Round(delta, 2) });
        }

        DrainTrayCommands();
        TrackLauncherWindow();

        _elapsedSeconds += delta;
        ReassertTopMostPeriodically(delta);

        // The provider needs this window's screen-space rectangle to map real windows into
        // overlay coordinates, so pumping it stays a host responsibility.
        _windowPlatforms.Pump(DateTimeOffset.UtcNow, OverlayScreenBounds(), VirtualScreenBounds());

        Vec2 pointer = PollPointer();
        _simulation.Update(delta, pointer, visible: _characterVisible);

        UpdateClickThrough();
        QueueRedraw();

        // Its own canvas item, so it needs its own invalidation; skipped entirely while hidden
        // rather than redrawn into nothing.
        if (_debugOverlay is { Visible: true })
        {
            _debugOverlay.QueueRedraw();
        }
    }

    /// <summary>Applies whatever the tray menu asked for. The menu runs on its own thread, so
    /// the work lands here, on Godot's, where the simulation and the window may be touched.</summary>
    private void DrainTrayCommands()
    {
        while (_tray is not null && _tray.TryDequeue(out TrayCommand command))
        {
            Apply(command);
        }
    }

    private void Apply(TrayCommand command)
    {
        _logger.Write("tray_command", new { command = command.ToString() });
        switch (command)
        {
            case TrayCommand.OpenLauncher:
                ShowLauncher();
                break;
            case TrayCommand.ShowCharacter:
                _characterVisible = true;
                Save(_settings with { CharactersVisible = true });
                break;
            case TrayCommand.HideCharacter:
                _characterVisible = false;
                Save(_settings with { CharactersVisible = false });
                break;
            case TrayCommand.Pause:
                _simulation!.IsPaused = true;
                Save(_settings with { IsPaused = true });
                break;
            case TrayCommand.Resume:
                _simulation!.IsPaused = false;
                Save(_settings with { IsPaused = false });
                break;
            case TrayCommand.IntensityCalm:
                SetIntensity("calm");
                break;
            case TrayCommand.IntensityNormal:
                SetIntensity("normal");
                break;
            case TrayCommand.IntensityActive:
                SetIntensity("active");
                break;
            case TrayCommand.ShowPlatformDebug:
                SetPlatformDebug(true);
                break;
            case TrayCommand.HidePlatformDebug:
                SetPlatformDebug(false);
                break;
            case TrayCommand.Quit:
                GetTree().Quit();
                break;
        }
    }

    private void SetPlatformDebug(bool enabled)
    {
        if (_debugOverlay is not null)
        {
            _debugOverlay.Visible = enabled;
        }

        Save(_settings with { ShowPlatformDebug = enabled });
    }

    private void SetIntensity(string intensity)
    {
        _simulation!.BehaviorIntensity = intensity;
        Save(_settings with { BehaviorIntensity = intensity });
    }

    /// <summary>Restores what the user chose last time, from the same file the WinForms host
    /// writes — both are the same application as far as the user is concerned.
    /// <para>
    /// Visibility is deliberately not restored. The WinForms host never shows the character on
    /// its own either; there it is the launcher's "release" button that does. This host has no
    /// launcher, so starting it IS that request — and honouring a stored "hidden" would leave a
    /// running overlay with nothing on screen to explain itself.
    /// </para>
    /// </summary>
    private void LoadSettings()
    {
        _settingsStore = new SettingsStore(Path.Combine(DataDirectory(), "settings.json"));
        _settings = _settingsStore.Load();
        _simulation!.IsPaused = _settings.IsPaused;
        _simulation.BehaviorIntensity = _settings.BehaviorIntensity;
        _simulation.CharacterScale = _settings.CharacterScale;
        _logger.Write("settings_loaded", new
        {
            paused = _settings.IsPaused,
            intensity = _settings.BehaviorIntensity,
            scale = _settings.CharacterScale,
        });
    }

    private void Save(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _settingsStore?.Save(settings);
        }
        catch (IOException exception)
        {
            // A settings file that cannot be written is not worth taking the character down for.
            _logger.Write("settings_save_failed", new { error = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Write("settings_save_failed", new { error = exception.Message });
        }
    }

    public override void _Draw()
    {
        if (_simulation is null)
        {
            return;
        }

        if (!_characterVisible)
        {
            return;
        }

        CharacterFrame frame = _simulation.CurrentFrame();
        ApplyHidingClip(frame);

        try
        {
            _renderer.Draw(this, frame);
        }
        catch (Exception ex)
        {
            // Same last-resort net as the WinForms host: whatever field goes bad next, a
            // renderer blow-up must not repeat every frame forever behind a borderless,
            // taskbar-less window with no way to close it.
            _simulation.NotifyRenderFailed(ex.ToString());
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_simulation is null)
        {
            return;
        }

        // Polled rather than taken from the event, so a grab is judged in exactly the same
        // coordinates that decided this window would accept the click in the first place.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            Vec2 pointer = PollPointer();
            if (button.Pressed)
            {
                bool grabbed = _simulation.TryGrab(pointer, _elapsedSeconds);
                RecordPointerEvent(grabbed ? "grab" : "miss");
            }
            else
            {
                _simulation.ReleaseGrab(pointer);
                RecordPointerEvent("release");
            }
        }
        else if (@event is InputEventMouseMotion && _simulation.IsHeld)
        {
            _simulation.Drag(PollPointer(), _elapsedSeconds);
            RecordPointerEvent("drag");
        }
    }

    private void RecordPointerEvent(string description)
    {
        if (_debugOverlay is not null)
        {
            _debugOverlay.LastPointerEvent = description;
        }
    }

    public override void _ExitTree()
    {
        _tray?.Dispose();
        _windowPlatforms?.Dispose();
    }

    /// <summary>Restricts mouse input to the character's own silhouette, so every click
    /// elsewhere lands on whatever is actually underneath the overlay.</summary>
    private void UpdateClickThrough()
    {
        if (_simulation is null)
        {
            return;
        }

        // Nothing is drawn while the character is hidden, so the overlay must not swallow a
        // single click either — it is an invisible full-screen window at that point.
        if (!_characterVisible)
        {
            SetClickThrough(true);
            return;
        }

        // While the character is held the window must keep taking mouse input even once the
        // pointer has left its silhouette, or a fast drag would drop it mid-flight.
        bool overCharacter = _simulation.HitTest(PollPointer());
        SetClickThrough(!_simulation.IsHeld && !overCharacter);
    }

    /// <summary>Toggles WS_EX_TRANSPARENT on the native handle — the same mechanism the
    /// WinForms host uses. Godot's own <c>WindowSetMousePassthrough</c> is not usable here:
    /// on Windows it is implemented with <c>SetWindowRgn</c>, which clips the window's
    /// *visible* region, so a region that follows a moving character leaves the window blank.
    /// <para>
    /// WS_EX_TRANSPARENT is re-asserted against the window's actual current style rather than
    /// against a cached flag, so that anything which rewrites the style cannot leave the
    /// overlay permanently swallowing input with no way back.
    /// </para>
    /// </summary>
    private void SetClickThrough(bool enabled)
    {
        if (_handle == 0)
        {
            return;
        }

        long actual = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        long target = enabled
            ? actual | WsExLayered | WsExTransparent
            : (actual | WsExLayered) & ~WsExTransparent;
        if (target == actual)
        {
            return;
        }

        SetWindowLongPtr(_handle, GwlExStyle, new nint(target));

        // Without this the style change is recorded but never applied, so the overlay keeps
        // swallowing every mouse event across the whole desktop — other windows cannot be
        // moved, resized or clicked at all while it runs.
        SetWindowPos(_handle, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Godot exposes no equivalent window flags, so the two behaviours the overlay
    /// depends on — never stealing keyboard focus, never appearing in Alt+Tab — are applied
    /// straight to the native handle, exactly as the WinForms host does through CreateParams.</summary>
    private void ApplyOverlayWindowStyles()
    {
        if (_handle == 0)
        {
            GD.PushError("[host] no native window handle — overlay styles not applied.");
            return;
        }

        long style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(
            _handle, GwlExStyle, new nint(style | WsExNoActivate | WsExToolWindow | WsExLayered));
        SetWindowPos(_handle, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        // WS_EX_LAYERED is what makes WS_EX_TRANSPARENT actually pass clicks to the window
        // underneath; on its own WS_EX_TRANSPARENT changes nothing, which is why every click
        // on the desktop was being swallowed. Measured on the live window: with the ex-style
        // at 0x080400B8 (transparent, not layered) WindowFromPoint returned this overlay for a
        // point nowhere near the character, and at 0x080C00B8 it returned the window below.
        //
        // Godot renders transparency through DWM rather than UpdateLayeredWindow, so the
        // constant alpha below has to be set explicitly to give the layered window defined
        // semantics; it does not flatten the per-pixel alpha — the character and the desktop
        // behind it both stay visible.
        SetLayeredWindowAttributes(_handle, 0, 255, LwaAlpha);
    }

    /// <summary>Another application asserting its own topmost status can silently push this
    /// window down the z-order; the character then keeps simulating but stops being visible,
    /// which reads as it having vanished. Re-asserting on an interval is the only reliable fix.</summary>
    private void ReassertTopMostPeriodically(double delta)
    {
        _topMostReassertSeconds += delta;
        if (_topMostReassertSeconds < TopMostReassertIntervalSeconds || _handle == 0)
        {
            return;
        }

        _topMostReassertSeconds = 0;
        SetWindowPos(_handle, HwndTopMost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    /// <summary>The overlay is always painted above every real window, so the only way for the
    /// character to read as being *behind* one while hiding is to not paint the covered part.
    /// <para>
    /// Clipping a canvas item takes BOTH calls below. A custom rect on its own only narrows the
    /// item's culling rectangle — the drawing is unaffected — which is why the hidden half of
    /// the body was still being painted in full, sticking out past the window. The pair is also
    /// left in place for the frame rather than reset afterwards: these are properties of the
    /// canvas item, read when it is rendered after <c>_Draw</c> returns, so clearing them at the
    /// end of <c>_Draw</c> would clear them before they ever applied.
    /// </para>
    /// </summary>
    private void ApplyHidingClip(CharacterFrame frame)
    {
        Rid item = GetCanvasItem();
        if (frame.HidingWallBounds is { } wall && TryVisibleSideOfWall(wall, frame.Body, out Rect2 visible))
        {
            RenderingServer.CanvasItemSetCustomRect(item, true, visible);
            RenderingServer.CanvasItemSetClip(item, true);
            return;
        }

        RenderingServer.CanvasItemSetClip(item, false);
        RenderingServer.CanvasItemSetCustomRect(item, false);
    }

    /// <summary>Godot clips a canvas item to one rectangle, not to an arbitrary "everything
    /// except this rectangle" region as the WinForms host does with <c>Region.Exclude</c>. The
    /// wall always sits to one side of a hiding character, so keeping only the half-plane on its
    /// visible side gives the same silhouette; the difference is that a character taller than
    /// the window also loses the part poking above or below it, which is the safe way to be
    /// wrong here — it hides slightly more rather than leaking the body through the wall.</summary>
    private static bool TryVisibleSideOfWall(RectD wall, RectD body, out Rect2 visible)
    {
        bool wallOnRight = wall.X >= body.X + (body.Width / 2);
        var left = (float)(wallOnRight ? body.X - body.Width : wall.Right);
        var right = (float)(wallOnRight ? wall.X : body.Right + body.Width);
        if (right <= left)
        {
            visible = default;
            return false;
        }

        visible = new Rect2(left, (float)(body.Y - body.Height), right - left, (float)(body.Height * 3));
        return true;
    }

    /// <summary>Polls the cursor from the display server rather than using
    /// <c>GetGlobalMousePosition</c>, which only reflects mouse events this window actually
    /// received. Once the overlay turns click-through it receives none, so that value freezes
    /// and the hit test would never notice the pointer returning to the character — it could
    /// then never be picked up again. This call keeps tracking the cursor while the window is
    /// click-through, confirmed by parking the cursor on a stationary character and watching
    /// the hit test fire and WS_EX_TRANSPARENT drop out of the window style.</summary>
    private Vec2 PollPointer()
    {
        Vector2I screenPoint = DisplayServer.MouseGetPosition();
        return new Vec2(screenPoint.X - _overlayOrigin.X, screenPoint.Y - _overlayOrigin.Y);
    }

    private RectD OverlayScreenBounds()
    {
        Vector2I size = GetTree().Root.Size;
        return new RectD(_overlayOrigin.X, _overlayOrigin.Y, size.X, size.Y);
    }

    private static RectD VirtualScreenBounds()
    {
        Rect2I desktop = GodotScreenGeometry.VirtualDesktop();
        return new RectD(desktop.Position.X, desktop.Position.Y, desktop.Size.X, desktop.Size.Y);
    }
}
