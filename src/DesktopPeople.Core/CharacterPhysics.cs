namespace DesktopPeople.Core;

public sealed class CharacterPhysics
{
    private const double Gravity = 1_850;
    private const double WalkSpeed = 92;
    private const double MaximumThrowSpeed = 1_600;

    public CharacterPhysics(Vec2 position, Size2 size)
    {
        Position = position;
        Size = size;
    }

    public Vec2 Position { get; private set; }

    public Vec2 Velocity { get; private set; }

    public Size2 Size { get; }

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
        double velocityX = Velocity.X;
        double velocityY = Velocity.Y;

        if (state == CharacterState.Walk)
        {
            velocityX = WalkDirection * WalkSpeed;
        }
        else if (state == CharacterState.Idle)
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
            WalkDirection = 1;
            hitHorizontalEdge = true;
        }
        else if (x + Size.Width > rightBoundary)
        {
            x = rightBoundary - Size.Width;
            velocityX = -Math.Abs(velocityX) * 0.55;
            WalkDirection = -1;
            hitHorizontalEdge = true;
        }

        Position = new Vec2(x, y);
        Velocity = new Vec2(velocityX, velocityY);
        return new CharacterMotionStep(previousBounds, Bounds, hitHorizontalEdge);
    }
}

public readonly record struct CharacterMotionStep(
    RectD PreviousBounds,
    RectD CurrentBounds,
    bool HitHorizontalEdge);

public readonly record struct PhysicsStepResult(bool Landed, bool HitHorizontalEdge)
{
    public static PhysicsStepResult None => new(false, false);
}
