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

    public PhysicsStepResult Step(
        double elapsedSeconds,
        CharacterState state,
        double floorY,
        double leftBoundary,
        double rightBoundary)
    {
        double delta = Math.Clamp(elapsedSeconds, 0, 0.05);
        if (state is CharacterState.HeldByMouse or CharacterState.Disabled)
        {
            return PhysicsStepResult.None;
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

        bool landed = false;
        if (y + Size.Height >= floorY && velocityY >= 0)
        {
            y = floorY - Size.Height;
            velocityY = 0;
            landed = state == CharacterState.Fall;
        }

        Position = new Vec2(x, y);
        Velocity = new Vec2(velocityX, velocityY);
        return new PhysicsStepResult(landed, hitHorizontalEdge);
    }
}

public readonly record struct PhysicsStepResult(bool Landed, bool HitHorizontalEdge)
{
    public static PhysicsStepResult None => new(false, false);
}

