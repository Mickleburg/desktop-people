namespace DesktopPeople.Core.Platforms;

/// <summary>
/// Whether a window's vertical edge is actually on screen at a given height, or buried under a
/// window stacked above it.
/// <para>
/// Top surfaces are already occlusion-clipped by <see cref="TopEdgeVisibilityPolicy"/>, but
/// nothing checked the side edges. The character would happily cling to the edge of a window
/// another window was covering, and — since the overlay always paints above everything — it
/// then appeared to be climbing thin air across the middle of whatever was on top.
/// </para>
/// </summary>
public static class WallEdgeVisibility
{
    /// <summary>Screen edges are exempt: they are synthetic, sit at the border of the display,
    /// and nothing can be stacked outside them.</summary>
    public static bool IsUsable(
        DesktopPlatform wall,
        double edgeX,
        double y,
        IReadOnlyList<DesktopPlatform> platforms)
    {
        if (wall.Kind != PlatformKind.Window)
        {
            return true;
        }

        foreach (DesktopPlatform occluder in platforms)
        {
            // Lower ZOrder means nearer the front, matching TopEdgeVisibilityPolicy.
            if (occluder.Id == wall.Id ||
                occluder.Kind != PlatformKind.Window ||
                occluder.ZOrder >= wall.ZOrder)
            {
                continue;
            }

            // Strictly inside: a window whose own edge is flush with this one leaves the edge
            // itself visible, and treating that as covered would rule out climbing between two
            // neatly tiled windows.
            if (edgeX > occluder.Bounds.X &&
                edgeX < occluder.Bounds.Right &&
                y >= occluder.Bounds.Y &&
                y <= occluder.Bounds.Bottom)
            {
                return false;
            }
        }

        return true;
    }
}
