using System.Collections.Immutable;

namespace DesktopPeople.Core.Platforms;

public interface IPlatformVisibilityPolicy
{
    ImmutableArray<DesktopPlatform> Apply(ImmutableArray<DesktopPlatform> platforms);
}

public sealed class TopEdgeVisibilityPolicy : IPlatformVisibilityPolicy
{
    private readonly double _minimumSegmentWidth;

    public TopEdgeVisibilityPolicy(double minimumSegmentWidth = 28)
    {
        _minimumSegmentWidth = minimumSegmentWidth;
    }

    public ImmutableArray<DesktopPlatform> Apply(ImmutableArray<DesktopPlatform> platforms)
    {
        var result = ImmutableArray.CreateBuilder<DesktopPlatform>(platforms.Length);
        foreach (DesktopPlatform platform in platforms)
        {
            var segments = new List<PlatformSegment>(platform.Segments);
            foreach (DesktopPlatform occluder in platforms)
            {
                double surfaceY = platform.Segments[0].SurfaceY;
                if (occluder.ZOrder >= platform.ZOrder ||
                    occluder.Id == platform.Id ||
                    occluder.Bounds.Y > surfaceY ||
                    occluder.Bounds.Bottom < surfaceY)
                {
                    continue;
                }

                segments = Subtract(segments, occluder.Bounds.X, occluder.Bounds.Right);
                if (segments.Count == 0)
                {
                    break;
                }
            }

            ImmutableArray<PlatformSegment> visibleSegments = segments
                .Where(segment => segment.Width >= _minimumSegmentWidth)
                .ToImmutableArray();
            if (!visibleSegments.IsEmpty)
            {
                result.Add(platform with { Segments = visibleSegments });
            }
        }

        return result.ToImmutable();
    }

    private static List<PlatformSegment> Subtract(
        IEnumerable<PlatformSegment> source,
        double occluderLeft,
        double occluderRight)
    {
        var result = new List<PlatformSegment>();
        foreach (PlatformSegment segment in source)
        {
            if (occluderRight <= segment.Left || occluderLeft >= segment.Right)
            {
                result.Add(segment);
                continue;
            }

            if (occluderLeft > segment.Left)
            {
                result.Add(segment with { Right = Math.Min(occluderLeft, segment.Right) });
            }

            if (occluderRight < segment.Right)
            {
                result.Add(segment with { Left = Math.Max(occluderRight, segment.Left) });
            }
        }

        return result;
    }
}
