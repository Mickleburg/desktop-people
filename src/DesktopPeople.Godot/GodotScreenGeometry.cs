using DesktopPeople.Core;
using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// Maps Godot's <see cref="DisplayServer"/> screen APIs into the overlay-relative coordinates
/// the simulation works in — the Godot counterpart of the WinForms host's own adapter.
/// <para>
/// The overlay window is positioned at the top-left of the virtual desktop, so overlay
/// coordinates are screen coordinates minus that origin.
/// </para>
/// </summary>
internal sealed class GodotScreenGeometry(Func<Vector2I> overlayOrigin) : IScreenGeometry
{
    /// <summary>
    /// Physical pixels per Godot canvas unit. Everything below — like the Win32 window
    /// provider the simulation shares with the WinForms host — speaks physical pixels, but a
    /// Godot window's size and its canvas are in logical units, which differ by 1.25 at 125%
    /// display scaling. The host keeps the simulation in physical pixels and applies this
    /// factor only when drawing and when reading the pointer; mixing the two put the
    /// character's floor several hundred units below the visible window, so it simulated and
    /// drew perfectly while being entirely off-screen.
    /// </summary>
    public static double UiScale => Math.Max(1.0, DisplayServer.ScreenGetDpi() / 96.0);

    public RectD VirtualBounds
    {
        get
        {
            Rect2I bounds = VirtualDesktop();
            Vector2I origin = overlayOrigin();
            return new RectD(
                bounds.Position.X - origin.X,
                bounds.Position.Y - origin.Y,
                bounds.Size.X,
                bounds.Size.Y);
        }
    }

    public int MonitorCount => DisplayServer.GetScreenCount();

    public RectD WorkAreaAt(Vec2 overlayPoint) =>
        ToOverlay(DisplayServer.ScreenGetUsableRect(ScreenAt(overlayPoint)));

    public double MonitorTopAt(Vec2 overlayPoint) =>
        DisplayServer.ScreenGetPosition(ScreenAt(overlayPoint)).Y - overlayOrigin().Y;

    public RectD PrimaryWorkArea =>
        ToOverlay(DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen()));

    /// <summary>Union of every attached monitor — Godot has no single "virtual screen" call,
    /// so it is assembled from the individual screen rectangles.</summary>
    public static Rect2I VirtualDesktop()
    {
        Rect2I total = new(
            DisplayServer.ScreenGetPosition(0),
            DisplayServer.ScreenGetSize(0));
        for (int screen = 1; screen < DisplayServer.GetScreenCount(); screen++)
        {
            total = total.Merge(new Rect2I(
                DisplayServer.ScreenGetPosition(screen),
                DisplayServer.ScreenGetSize(screen)));
        }

        return total;
    }

    private int ScreenAt(Vec2 overlayPoint)
    {
        Vector2I origin = overlayOrigin();
        var screenPoint = new Vector2I(
            origin.X + (int)overlayPoint.X,
            origin.Y + (int)overlayPoint.Y);
        return DisplayServer.GetScreenFromRect(new Rect2(screenPoint, Vector2.One));
    }

    private RectD ToOverlay(Rect2I screenRect)
    {
        Vector2I origin = overlayOrigin();
        return new RectD(
            screenRect.Position.X - origin.X,
            screenRect.Position.Y - origin.Y,
            screenRect.Size.X,
            screenRect.Size.Y);
    }
}

/// <summary>Routes the host's and the simulation's structured events into Godot's own output
/// and, when given one, into a file as well. Both matter and for different reasons: the console
/// is what you watch while running from the editor, the file is what still exists tomorrow when
/// the user reports that the character did something strange an hour ago.</summary>
internal sealed class GodotLogger(IOverlayLogger? file = null) : IOverlayLogger
{
    public void Write(string eventName, object? data = null)
    {
        GD.Print(data is null ? $"[log] {eventName}" : $"[log] {eventName} {data}");
        file?.Write(eventName, data);
    }
}
