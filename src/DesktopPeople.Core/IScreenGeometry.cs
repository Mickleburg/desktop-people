namespace DesktopPeople.Core;

/// <summary>
/// Monitor layout, expressed entirely in overlay coordinates (the overlay window's own
/// client space), which is the only coordinate system the simulation ever works in.
/// <para>
/// This is the single seam that kept the character's behaviour welded to WinForms:
/// <c>Screen.FromPoint</c>, <c>Screen.PrimaryScreen</c> and
/// <c>SystemInformation.VirtualScreen</c> were called directly from the middle of the
/// physics loop, so none of that logic could run — or be tested — outside a live
/// <c>Form</c>. Each host implements this against its own windowing API instead.
/// </para>
/// </summary>
public interface IScreenGeometry
{
    /// <summary>The whole virtual desktop, in overlay coordinates.</summary>
    RectD VirtualBounds { get; }

    /// <summary>How many physical monitors are attached (diagnostics only).</summary>
    int MonitorCount { get; }

    /// <summary>Work area — desktop minus taskbar — of the monitor containing
    /// <paramref name="overlayPoint"/>. This is the surface the character walks on.</summary>
    RectD WorkAreaAt(Vec2 overlayPoint);

    /// <summary>Physical top edge of the monitor containing <paramref name="overlayPoint"/>.
    /// Distinct from the work area's top, which excludes a top-docked taskbar: head-room
    /// checks care about the real monitor edge.</summary>
    double MonitorTopAt(Vec2 overlayPoint);

    /// <summary>Work area of the primary monitor, where a fresh character is dropped in.</summary>
    RectD PrimaryWorkArea { get; }
}
