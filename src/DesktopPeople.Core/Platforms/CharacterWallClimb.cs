namespace DesktopPeople.Core.Platforms;

public enum WallSide
{
    Left,
    Right,
}

public enum ClimbOutcome
{
    InProgress,
    ReachedTop,
    ReachedBottom,
}

public readonly record struct ClimbStep(Vec2 Position, ClimbOutcome Outcome);

/// <summary>
/// Tracks a character clinging to one vertical edge of the platform it was just
/// standing on and sliding along it. Only the currently-attached platform's own
/// bounds are used, so no separate "nearby wall" search is needed.
/// </summary>
public sealed class CharacterWallClimb
{
    /// <summary>Vertical distance, near the top, over which the character eases sideways
    /// from clinging against the wall's outer face onto standing flush on the ledge —
    /// instead of the two positions being an instant, same-frame swap in either
    /// direction.</summary>
    private const double LedgeBlendDistance = 24;

    private double _wallX;
    private double _ledgeX;
    private double _topY;
    private double _bottomY;

    public bool IsClimbing { get; private set; }

    public string? PlatformId { get; private set; }

    public WallSide Side { get; private set; }

    public void Start(DesktopPlatform platform, WallSide side, Size2 characterSize)
    {
        IsClimbing = true;
        PlatformId = platform.Id;
        Side = side;
        _wallX = side == WallSide.Left
            ? platform.Bounds.X - characterSize.Width
            : platform.Bounds.Right;
        _ledgeX = side == WallSide.Left
            ? platform.Bounds.X
            : platform.Bounds.Right - characterSize.Width;
        _topY = platform.Segments[0].SurfaceY - characterSize.Height;
        _bottomY = platform.Bounds.Bottom - characterSize.Height;
    }

    public void Stop()
    {
        IsClimbing = false;
        PlatformId = null;
    }

    /// <summary>Advances climbing by <paramref name="deltaY"/> (positive = downward).</summary>
    public ClimbStep Advance(double currentY, double deltaY)
    {
        double y = currentY + deltaY;
        if (y <= _topY)
        {
            return new ClimbStep(new Vec2(_ledgeX, _topY), ClimbOutcome.ReachedTop);
        }

        if (y >= _bottomY)
        {
            return new ClimbStep(new Vec2(_wallX, _bottomY), ClimbOutcome.ReachedBottom);
        }

        double blend = Math.Clamp((y - _topY) / LedgeBlendDistance, 0, 1);
        double x = _ledgeX + ((_wallX - _ledgeX) * blend);
        return new ClimbStep(new Vec2(x, y), ClimbOutcome.InProgress);
    }
}
