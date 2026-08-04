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

    /// <summary>How much of the body is actually against the wall's face: 1 while clinging to
    /// it, easing to 0 as <see cref="Advance"/> transfers the character sideways onto the ledge
    /// near the top. Renderers scale the climbing pose by this, because that transfer moves the
    /// body a full body width away from the wall while the climb is still in progress — with
    /// the pose left at full strength the limbs went on reaching for a wall the character was
    /// no longer beside, at both ends of every climb.</summary>
    public double WallContact { get; private set; }

    public void Start(DesktopPlatform platform, WallSide side, Size2 characterSize, RectD? screenBounds = null)
    {
        IsClimbing = true;
        PlatformId = platform.Id;
        Side = side;

        // A climb starts from standing on the platform, so the first Advance begins the ledge
        // transfer from zero contact; it corrects this on the same frame in any other case.
        WallContact = 0;
        Retarget(platform, characterSize, screenBounds);
    }

    /// <summary>Re-syncs the clung-to wall's geometry to the platform's current bounds —
    /// callers should invoke this every frame a climb is in progress (not just at Start),
    /// otherwise a window dragged or resized mid-climb leaves the character clinging to
    /// wherever the window used to be instead of where it actually is now.</summary>
    public void Retarget(DesktopPlatform platform, Size2 characterSize, RectD? screenBounds = null)
    {
        _wallX = Side == WallSide.Left
            ? platform.Bounds.X - characterSize.Width
            : platform.Bounds.Right;
        _ledgeX = Side == WallSide.Left
            ? platform.Bounds.X
            : platform.Bounds.Right - characterSize.Width;
        _topY = platform.Segments[0].SurfaceY - characterSize.Height;
        _bottomY = platform.Bounds.Bottom - characterSize.Height;

        if (screenBounds is { } bounds)
        {
            // A window's real geometry can hang off the side or bottom of the monitor
            // (dragged mostly past the edge of the display) — without this, clinging to
            // that edge let the character climb out into that off-screen space with
            // nothing to stop it, only to snap back the instant grounded physics resumed
            // and re-clamped it on-screen. The wall/ledge X and the bottom of the climb are
            // never allowed past the visible screen, regardless of where the real window
            // geometry actually extends to.
            double minX = bounds.X;
            double maxX = bounds.Right - characterSize.Width;
            if (maxX >= minX)
            {
                _wallX = Math.Clamp(_wallX, minX, maxX);
                _ledgeX = Math.Clamp(_ledgeX, minX, maxX);
            }

            _topY = Math.Max(_topY, bounds.Y);
            double maxBottomY = bounds.Bottom - characterSize.Height;
            if (maxBottomY >= _topY)
            {
                _bottomY = Math.Min(_bottomY, maxBottomY);
            }
        }
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
            WallContact = 0;
            return new ClimbStep(new Vec2(_ledgeX, _topY), ClimbOutcome.ReachedTop);
        }

        if (y >= _bottomY)
        {
            WallContact = 1;
            return new ClimbStep(new Vec2(_wallX, _bottomY), ClimbOutcome.ReachedBottom);
        }

        double blend = Math.Clamp((y - _topY) / LedgeBlendDistance, 0, 1);
        WallContact = blend;
        double x = _ledgeX + ((_wallX - _ledgeX) * blend);
        return new ClimbStep(new Vec2(x, y), ClimbOutcome.InProgress);
    }
}
