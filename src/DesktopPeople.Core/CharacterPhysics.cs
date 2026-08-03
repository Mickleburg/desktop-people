namespace DesktopPeople.Core;

public sealed class CharacterPhysics
{
    private const double Gravity = 1_850;
    private const double WalkSpeed = 92;
    private const double RunSpeed = 190;
    private const double MaximumThrowSpeed = 1_600;

    public CharacterPhysics(Vec2 position, Size2 size)
    {
        Position = position;
        Size = size;
    }

    public Vec2 Position { get; private set; }

    public Vec2 Velocity { get; private set; }

    public Size2 Size { get; private set; }

    public int WalkDirection { get; private set; } = 1;

    public RectD Bounds => new(Position.X, Position.Y, Size.Width, Size.Height);

    public void HoldAt(Vec2 pointer)
    {
        Position = new Vec2(pointer.X - (Size.Width / 2), pointer.Y - (Size.Height / 2));
        Velocity = Vec2.Zero;
    }

    public void Release(Vec2 velocity)
    {
        Velocity = velocity.ClampMagnitude(MaximumThrowSpeed);
    }

    public void Nudge(Vec2 velocity)
    {
        Velocity = velocity.ClampMagnitude(MaximumThrowSpeed);
    }

    public void SetPosition(Vec2 position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
    }

    public void LandOn(double surfaceY)
    {
        Position = new Vec2(Position.X, surfaceY - Size.Height);
        Velocity = new Vec2(Velocity.X, 0);
    }

    public void BonkCeiling(double ceilingY)
    {
        Position = new Vec2(Position.X, ceilingY);
        Velocity = new Vec2(Velocity.X, 0);
    }

    /// <summary>Resizes the character in place, keeping its feet planted at the same
    /// bottom-center point so a live scale change doesn't shift it off its platform.</summary>
    public void Rescale(Size2 newSize)
    {
        double feetCenterX = Position.X + (Size.Width / 2);
        double feetBottomY = Position.Y + Size.Height;
        Size = newSize;
        Position = new Vec2(feetCenterX - (newSize.Width / 2), feetBottomY - newSize.Height);
    }

    /// <summary>Overrides which way the character is facing/walking, e.g. to flee a cursor.</summary>
    public void FaceDirection(int direction) => WalkDirection = direction >= 0 ? 1 : -1;

    public PhysicsStepResult Step(
        double elapsedSeconds,
        CharacterState state,
        double floorY,
        double leftBoundary,
        double rightBoundary)
    {
        CharacterMotionStep motion = Integrate(
            elapsedSeconds,
            state,
            leftBoundary,
            rightBoundary);
        bool landed = false;
        if (Position.Y + Size.Height >= floorY && Velocity.Y >= 0)
        {
            LandOn(floorY);
            landed = state == CharacterState.Fall;
        }

        return new PhysicsStepResult(landed, motion.HitHorizontalEdge);
    }

    public CharacterMotionStep Integrate(
        double elapsedSeconds,
        CharacterState state,
        double leftBoundary,
        double rightBoundary)
    {
        RectD previousBounds = Bounds;
        double delta = Math.Clamp(elapsedSeconds, 0, 0.05);
        if (state is CharacterState.HeldByMouse or CharacterState.Disabled)
        {
            return new CharacterMotionStep(previousBounds, previousBounds, false);
        }

        bool hitHorizontalEdge = false;
        int hitEdgeDirection = 0;
        double velocityX = Velocity.X;
        double velocityY = Velocity.Y;

        if (state == CharacterState.Walk)
        {
            velocityX = WalkDirection * WalkSpeed;
        }
        else if (state == CharacterState.Run)
        {
            velocityX = WalkDirection * RunSpeed;
        }
        else if (state is CharacterState.Idle or CharacterState.Sit or CharacterState.Climb or CharacterState.Hide)
        {
            velocityX = 0;
            velocityY = 0;
        }
        else
        {
            velocityY += Gravity * delta;
            velocityX *= Math.Pow(0.985, delta * 60);
        }

        double x = Position.X + (velocityX * delta);
        double y = Position.Y + (velocityY * delta);

        if (x < leftBoundary)
        {
            x = leftBoundary;
            velocityX = Math.Abs(velocityX) * 0.55;
            hitHorizontalEdge = true;
            hitEdgeDirection = -1;
            // This bounce turnaround (for free autonomous wandering) flips WalkDirection
            // itself, so callers that need to know which edge was actually hit this frame
            // can't rely on reading WalkDirection afterward — hitEdgeDirection preserves the
            // pre-bounce approach direction for that (see CharacterMotionStep.HitEdgeDirection).
            WalkDirection = 1;
        }
        else if (x + Size.Width > rightBoundary)
        {
            x = rightBoundary - Size.Width;
            velocityX = -Math.Abs(velocityX) * 0.55;
            hitHorizontalEdge = true;
            hitEdgeDirection = 1;
            WalkDirection = -1;
        }

        Position = new Vec2(x, y);
        Velocity = new Vec2(velocityX, velocityY);
        return new CharacterMotionStep(previousBounds, Bounds, hitHorizontalEdge, hitEdgeDirection);
    }
}

public readonly record struct CharacterMotionStep(
    RectD PreviousBounds,
    RectD CurrentBounds,
    bool HitHorizontalEdge,
    int HitEdgeDirection = 0);

public readonly record struct PhysicsStepResult(bool Landed, bool HitHorizontalEdge)
{
    public static PhysicsStepResult None => new(false, false);
}
