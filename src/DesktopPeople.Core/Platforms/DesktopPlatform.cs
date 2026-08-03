using System.Collections.Immutable;

namespace DesktopPeople.Core.Platforms;

public enum PlatformKind
{
    Window,
    Desktop,

    /// <summary>Synthetic wall standing for a monitor's own left/right work-area edge, so
    /// the character can climb the side of the screen itself the same way it climbs a
    /// window's vertical edge — never produced by the real window platform provider.</summary>
    ScreenEdge,
}

public readonly record struct PlatformSegment(double Left, double Right, double SurfaceY)
{
    public double Width => Right - Left;

    public bool Intersects(double left, double right) => right >= Left && left <= Right;

    public bool Contains(double x) => x >= Left && x <= Right;
}

public sealed record DesktopPlatform
{
    public required string Id { get; init; }

    public required PlatformKind Kind { get; init; }

    public long ExternalHandle { get; init; }

    public required RectD Bounds { get; init; }

    public required ImmutableArray<PlatformSegment> Segments { get; init; }

    /// <summary>The platform's underside, for jump collisions coming from below.</summary>
    public ImmutableArray<PlatformSegment> CeilingSegments { get; init; } = [];

    public int ZOrder { get; init; }

    public string MonitorId { get; init; } = string.Empty;

    /// <summary>Overlay-space Y above which the character's head would leave the physical monitor.</summary>
    public double MonitorTop { get; init; } = double.NegativeInfinity;

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PlatformSnapshot
{
    public static PlatformSnapshot Empty { get; } = new()
    {
        Platforms = [],
        CapturedAt = DateTimeOffset.MinValue,
    };

    public required ImmutableArray<DesktopPlatform> Platforms { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public int EnumeratedWindowCount { get; init; }

    public TimeSpan UpdateDuration { get; init; }
}
