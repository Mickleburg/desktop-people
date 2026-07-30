namespace DesktopPeople.Core.Platforms;

public sealed class PlatformCollisionResolver
{
    private readonly double _footWidthRatio;

    public PlatformCollisionResolver(double footWidthRatio = 0.42)
    {
        _footWidthRatio = Math.Clamp(footWidthRatio, 0.1, 1);
    }

    public PlatformCollision? ResolveDownward(
        RectD previousBounds,
        RectD currentBounds,
        double verticalVelocity,
        IReadOnlyList<DesktopPlatform> platforms)
    {
        if (verticalVelocity <= 0 || currentBounds.Bottom < previousBounds.Bottom)
        {
            return null;
        }

        (double footLeft, double footRight) = GetFootInterval(currentBounds);
        PlatformCollision? closest = null;
        foreach (DesktopPlatform platform in platforms)
        {
            foreach (PlatformSegment segment in platform.Segments)
            {
                if (segment.SurfaceY < previousBounds.Bottom - 0.01 ||
                    segment.SurfaceY > currentBounds.Bottom + 0.01 ||
                    !segment.Intersects(footLeft, footRight))
                {
                    continue;
                }

                if (closest is null || segment.SurfaceY < closest.Value.Segment.SurfaceY)
                {
                    closest = new PlatformCollision(platform, segment);
                }
            }
        }

        return closest;
    }

    public (double Left, double Right) GetFootInterval(RectD bounds)
    {
        double footWidth = bounds.Width * _footWidthRatio;
        double center = bounds.X + (bounds.Width / 2);
        return (center - (footWidth / 2), center + (footWidth / 2));
    }
}

public readonly record struct PlatformCollision(
    DesktopPlatform Platform,
    PlatformSegment Segment);
