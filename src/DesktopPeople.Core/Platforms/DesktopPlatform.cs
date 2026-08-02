using System.Collections.Immutable;

namespace DesktopPeople.Core.Platforms;

public enum PlatformKind
{
    Window,
    Desktop,
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
