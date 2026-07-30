namespace DesktopPeople.Core.Platforms;

public sealed class CharacterPlatformAttachment
{
    private readonly double _footWidthRatio;

    public CharacterPlatformAttachment(double footWidthRatio = 0.42)
    {
        _footWidthRatio = footWidthRatio;
    }

    public bool IsAttached => PlatformId is not null;

    public string? PlatformId { get; private set; }

    public double RelativeFootCenterX { get; private set; }

    public double VerticalOffset { get; private set; }

    public RectD LastPlatformBounds { get; private set; }

    public void Attach(DesktopPlatform platform, RectD characterBounds)
    {
        PlatformId = platform.Id;
        RelativeFootCenterX =
            characterBounds.X + (characterBounds.Width / 2) - platform.Bounds.X;
        PlatformSegment segment = FindSupportingSegment(platform, characterBounds)
            ?? throw new InvalidOperationException("Character is not supported by this platform.");
        VerticalOffset = segment.SurfaceY - characterBounds.Bottom;
        LastPlatformBounds = platform.Bounds;
    }

    public bool TryFollow(
        DesktopPlatform platform,
        RectD characterBounds,
        out Vec2 targetPosition)
    {
        targetPosition = new Vec2(characterBounds.X, characterBounds.Y);
        if (PlatformId != platform.Id)
        {
            return false;
        }

        double footCenter = platform.Bounds.X + RelativeFootCenterX;
        double x = footCenter - (characterBounds.Width / 2);
        RectD horizontalCandidate = new(x, characterBounds.Y, characterBounds.Width, characterBounds.Height);
        PlatformSegment? segment = FindSupportingSegment(platform, horizontalCandidate);
        if (segment is null)
        {
            return false;
        }

        double y = segment.Value.SurfaceY - characterBounds.Height - VerticalOffset;
        targetPosition = new Vec2(x, y);
        LastPlatformBounds = platform.Bounds;
        return true;
    }

    public void Sync(DesktopPlatform platform, RectD characterBounds)
    {
        if (PlatformId != platform.Id)
        {
            return;
        }

        RelativeFootCenterX =
            characterBounds.X + (characterBounds.Width / 2) - platform.Bounds.X;
        LastPlatformBounds = platform.Bounds;
    }

    public void Detach()
    {
        PlatformId = null;
        RelativeFootCenterX = 0;
        VerticalOffset = 0;
        LastPlatformBounds = default;
    }

    public PlatformSegment? FindSupportingSegment(
        DesktopPlatform platform,
        RectD characterBounds)
    {
        double footWidth = characterBounds.Width * _footWidthRatio;
        double center = characterBounds.X + (characterBounds.Width / 2);
        double left = center - (footWidth / 2);
        double right = center + (footWidth / 2);
        foreach (PlatformSegment segment in platform.Segments)
        {
            if (segment.Intersects(left, right))
            {
                return segment;
            }
        }

        return null;
    }
}
