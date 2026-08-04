namespace DesktopPeople.Core.Platforms;

/// <summary>
/// Finds windows that rise up across the surface line a character is currently walking
/// on — a second, unrelated window standing "in the way" rather than the edge of the
/// platform the character is actually attached to. These act as walls: horizontal motion
/// must stop at them (not just wherever the walked-on platform's own, occlusion-clipped
/// segment happens to end), and they're candidates to climb or hide behind.
/// </summary>
public static class WallEncounterDetector
{
    public readonly record struct Neighbor(DesktopPlatform Platform, double Boundary);

    public readonly record struct Neighbors(Neighbor? Left, Neighbor? Right);

    public static Neighbors FindNeighborWalls(
        DesktopPlatform floor,
        RectD characterBounds,
        IReadOnlyList<DesktopPlatform> platforms)
    {
        Neighbor? left = null;
        Neighbor? right = null;
        double footY = characterBounds.Bottom;
        double centerX = characterBounds.X + (characterBounds.Width / 2);
        double centerY = characterBounds.Y + (characterBounds.Height / 2);

        foreach (DesktopPlatform candidate in platforms)
        {
            if (candidate.Id == floor.Id ||
                candidate.Kind != PlatformKind.Window ||
                candidate.Bounds.Y > footY ||
                candidate.Bounds.Bottom < footY)
            {
                continue;
            }

            if (candidate.Bounds.Right <= centerX)
            {
                if (!WallEdgeVisibility.IsUsable(candidate, candidate.Bounds.Right, centerY, platforms))
                {
                    continue;
                }

                if (left is null || candidate.Bounds.Right > left.Value.Boundary)
                {
                    left = new Neighbor(candidate, candidate.Bounds.Right);
                }
            }
            else if (candidate.Bounds.X >= centerX)
            {
                if (!WallEdgeVisibility.IsUsable(candidate, candidate.Bounds.X, centerY, platforms))
                {
                    continue;
                }

                if (right is null || candidate.Bounds.X < right.Value.Boundary)
                {
                    right = new Neighbor(candidate, candidate.Bounds.X);
                }
            }
        }

        return new Neighbors(left, right);
    }
}
