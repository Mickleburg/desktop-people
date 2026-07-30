namespace DesktopPeople.Core;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero => new(0, 0);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public static Vec2 operator +(Vec2 left, Vec2 right) =>
        new(left.X + right.X, left.Y + right.Y);

    public static Vec2 operator -(Vec2 left, Vec2 right) =>
        new(left.X - right.X, left.Y - right.Y);

    public static Vec2 operator *(Vec2 value, double scalar) =>
        new(value.X * scalar, value.Y * scalar);

    public Vec2 ClampMagnitude(double maximum)
    {
        double length = Length;
        return length <= maximum || length == 0
            ? this
            : this * (maximum / length);
    }
}

public readonly record struct Size2(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(Vec2 point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public RectD Inflate(double horizontal, double vertical) =>
        new(X - horizontal, Y - vertical, Width + (horizontal * 2), Height + (vertical * 2));
}

