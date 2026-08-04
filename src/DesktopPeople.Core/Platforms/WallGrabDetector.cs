namespace DesktopPeople.Core.Platforms;

/// <summary>
/// Detects whether an airborne character (mid-jump or otherwise falling/rising) is close
/// enough to a window's vertical edge to attempt grabbing on, so a jump aimed at a wall can
/// end in a climb instead of always sailing past or bonking a ceiling.
/// </summary>
public static class WallGrabDetector
{
    public readonly record struct Reach(DesktopPlatform Platform, WallSide Side);

    public static Reach? FindReachableEdge(
        RectD characterBounds,
        double velocityX,
        IReadOnlyList<DesktopPlatform> platforms,
        double captureDistance)
    {
        if (Math.Abs(velocityX) < 1)
        {
            return null;
        }

        double centerY = characterBounds.Y + (characterBounds.Height / 2);
        DesktopPlatform? best = null;
        WallSide bestSide = WallSide.Left;
        double bestDistance = double.MaxValue;

        foreach (DesktopPlatform platform in platforms)
        {
            // Anything that isn't a flat floor is a candidate wall — this covers both real
            // windows and the synthetic screen-edge platforms OverlayForm feeds in here so a
            // thrown/falling character can catch the side of the monitor too.
            if (platform.Kind == PlatformKind.Desktop ||
                centerY < platform.Bounds.Y ||
                centerY > platform.Bounds.Bottom)
            {
                continue;
            }

            double edgeX = velocityX > 0 ? platform.Bounds.X : platform.Bounds.Right;
            double distance = velocityX > 0
                ? platform.Bounds.X - characterBounds.Right
                : characterBounds.X - platform.Bounds.Right;
            if (distance < -2 || distance > captureDistance || distance >= bestDistance)
            {
                continue;
            }

            // No grabbing an edge that another window is covering — there is nothing visible
            // there to hold on to.
            if (!WallEdgeVisibility.IsUsable(platform, edgeX, centerY, platforms))
            {
                continue;
            }

            bestDistance = distance;
            best = platform;
            bestSide = WallSideResolver.ForEncounteredWall(velocityX);
        }

        return best is null ? null : new Reach(best, bestSide);
    }
}
