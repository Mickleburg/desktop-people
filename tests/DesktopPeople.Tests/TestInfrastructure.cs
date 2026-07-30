namespace DesktopPeople.Tests;

internal readonly record struct TestCase(string Name, Action Execute);

internal static class AssertEx
{
    public static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    public static void False(bool value) => True(!value);

    public static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    public static void Near(double expected, double actual, double tolerance = 0.001)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {expected} ± {tolerance}, received {actual}.");
        }
    }

    public static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
