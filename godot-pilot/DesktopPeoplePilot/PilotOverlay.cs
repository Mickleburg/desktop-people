using System.Runtime.InteropServices;
using Godot;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;
using DesktopPeople.Windows;

namespace DesktopPeoplePilot;

/// <summary>
/// Pilot spike for the 3b Godot migration scoping (see docs/DECISIONS.md ADR-001 and
/// docs/ROADMAP.md "Инкремент 3b"). Proves, with a real running process rather than just
/// documentation research, that:
/// 1. Godot 4 can host a transparent, borderless, always-on-top window with a
///    per-frame mouse-passthrough polygon around a stand-in rectangle (the selective
///    click-through the real app needs instead of the WinForms TransparencyKey approach).
/// 2. DesktopPeople.Windows/DesktopPeople.Core — unmodified, referenced via a plain
///    ProjectReference — run live inside a Godot-hosted .NET runtime, not just WinForms.
/// Throwaway scaffolding, not part of the shipping app.
/// </summary>
public sealed partial class PilotOverlay : Control
{
    private static readonly Rect2 StandInRect = new(new Vector2(120, 60), new Vector2(60, 114));
    private const double PlatformLogIntervalSeconds = 5;

    // WS_EX_NOACTIVATE stops the overlay stealing keyboard focus from whatever the user is
    // typing in when the character is clicked/dragged; WS_EX_TOOLWINDOW keeps it out of the
    // Alt+Tab list. OverlayForm already applies both via CreateParams — Godot has no
    // equivalent knob, so the migration has to reach the native HWND and set them directly,
    // which is exactly what this pilot is here to prove is possible.
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    // Applying WS_EX_NOACTIVATE means this window stops receiving keyboard focus, so the ESC
    // handler below can no longer be relied on — and WS_EX_TOOLWINDOW simultaneously removes
    // the Alt+Tab escape hatch that was the only way out the first time this pilot went
    // wrong. A hard time limit guarantees the pilot can never strand the user again,
    // whatever else breaks.
    private const double FailsafeQuitSeconds = 90;

    private WindowsWindowPlatformProvider? _windowPlatforms;
    private double _logElapsedSeconds;
    private double _aliveSeconds;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    public override void _Ready()
    {
        Window window = GetTree().Root;
        // TransparentBg must be set on the ROOT window itself (Window extends Viewport), not
        // via GetViewport() — on the root, that call does not reliably resolve back to this
        // same viewport, so the clear color kept being painted opaque. Setting the project
        // settings alone is documented as insufficient; this runtime call is the actual fix
        // most working examples rely on.
        window.TransparentBg = true;
        window.Transparent = true;
        window.Borderless = true;
        window.AlwaysOnTop = true;

        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        UpdateMousePassthrough();

        var registry = new PlatformRegistry();
        _windowPlatforms = new WindowsWindowPlatformProvider(
            new Win32WindowApi(),
            new Win32WindowEventSource(),
            registry,
            System.Environment.ProcessId);

        RectD overlayBounds = CurrentWindowBounds(window);
        _windowPlatforms.Start(overlayBounds, overlayBounds);
        _windowPlatforms.MetricsUpdated += metrics =>
        {
            if (metrics.WasReconciliation)
            {
                GD.Print($"[pilot] enumerated={metrics.EnumeratedWindowCount} platforms={metrics.PlatformCount} update_ms={metrics.UpdateDuration.TotalMilliseconds:F2}");
            }
        };

        ApplyOverlayWindowStyles();

        GD.Print("[pilot] DesktopPeople.Windows/Core referenced and running live inside Godot.");
        GD.Print($"[pilot] Auto-quits after {FailsafeQuitSeconds:F0}s (ESC may not work once WS_EX_NOACTIVATE is applied).");
    }

    /// <summary>Reaches through Godot to the real Win32 HWND and applies the same two
    /// extended styles <see cref="OverlayForm"/> sets through CreateParams. Godot exposes no
    /// equivalent window flags, so if this did not work the migration would lose both
    /// behaviours — hence proving it here, before porting anything real.</summary>
    private void ApplyOverlayWindowStyles()
    {
        // Godot hands the HWND back as a long regardless of platform pointer width.
        nint handle = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (handle == 0)
        {
            GD.PushError("[pilot] no native window handle — cannot apply overlay styles.");
            return;
        }

        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        long updated = style | WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(handle, GwlExStyle, new nint(updated));
        SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        GD.Print($"[pilot] applied WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW to hwnd 0x{handle:X} (ex-style 0x{style:X} -> 0x{updated:X}).");
    }

    public override void _Input(InputEvent @event)
    {
        // The whole point of a borderless, always-on-top overlay is that it has no title
        // bar to close from — the real app relies on the tray icon for that, which this
        // throwaway pilot doesn't have. Without an explicit way out, a rendering bug (like
        // the Forward+/Vulkan transparency issue this pilot originally hit — see
        // project.godot) turns into "stuck full-screen window, no visible way to close it,"
        // which is exactly the class of problem 3a.9-3a.11 were about fixing in the real app.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        if (_windowPlatforms is null)
        {
            return;
        }

        _aliveSeconds += delta;
        if (_aliveSeconds >= FailsafeQuitSeconds)
        {
            GD.Print("[pilot] failsafe reached — quitting.");
            GetTree().Quit();
            return;
        }

        QueueRedraw();

        RectD overlayBounds = CurrentWindowBounds(GetTree().Root);
        _windowPlatforms.Pump(DateTimeOffset.UtcNow, overlayBounds, overlayBounds);

        _logElapsedSeconds += delta;
        if (_logElapsedSeconds >= PlatformLogIntervalSeconds)
        {
            _logElapsedSeconds = 0;
            PlatformSnapshot snapshot = _windowPlatforms.Snapshot;
            GD.Print($"[pilot] live platform snapshot: {snapshot.Platforms.Length} platform(s)");
        }
    }

    public override void _Draw()
    {
        // The stand-in for CharacterRenderer's output — just proves something paints
        // correctly on the transparent surface at the exact rect the passthrough polygon
        // also uses, so the two can be visually cross-checked against each other.
        DrawRect(StandInRect, new Color(0.44f, 0.36f, 1f, 1f));
        DrawRect(StandInRect, new Color(0.16f, 0.18f, 0.24f, 1f), filled: false, width: 3);

        // Drawn on its own opaque plate in a high-contrast colour: the overlay sits over
        // whatever the user's desktop happens to be, so plain text can land dark-on-dark and
        // become unreadable — which would defeat the point of advertising the only way out.
        Font font = ThemeDB.FallbackFont;
        double remaining = Math.Max(0, FailsafeQuitSeconds - _aliveSeconds);
        string hint = $"DesktopPeoplePilot — closes automatically in {remaining:F0}s";
        var hintOrigin = new Vector2(StandInRect.Position.X, StandInRect.End.Y + 32);
        Vector2 hintSize = font.GetStringSize(hint, fontSize: 20);
        DrawRect(
            new Rect2(hintOrigin - new Vector2(8, hintSize.Y), hintSize + new Vector2(16, 12)),
            new Color(0f, 0f, 0f, 0.75f));
        DrawString(font, hintOrigin, hint, fontSize: 20, modulate: new Color(1f, 0.95f, 0.3f));
    }

    private void UpdateMousePassthrough()
    {
        Vector2[] polygon =
        [
            StandInRect.Position,
            new Vector2(StandInRect.End.X, StandInRect.Position.Y),
            StandInRect.End,
            new Vector2(StandInRect.Position.X, StandInRect.End.Y),
        ];
        DisplayServer.WindowSetMousePassthrough(polygon);
    }

    private static RectD CurrentWindowBounds(Window window) =>
        new(0, 0, window.Size.X, window.Size.Y);

    public override void _ExitTree()
    {
        _windowPlatforms?.Dispose();
    }
}
