namespace DesktopPeople.Core;

/// <summary>
/// Accumulates how much the cursor has been pestering the character — lingering
/// close by, or repeated grabs/clicks — and decays it when left alone. Crossing
/// <see cref="IsFleeing"/> is the cue for the character to try to get away.
/// </summary>
public sealed class CharacterHarassmentTracker
{
    private readonly double _proximityRadius;
    private readonly double _fleeThreshold;
    private readonly double _decayPerSecond;
    private readonly double _clickBump;

    public CharacterHarassmentTracker(
        double proximityRadius = 70,
        double fleeThreshold = 6,
        double decayPerSecond = 0.6,
        double clickBump = 1.5)
    {
        _proximityRadius = proximityRadius;
        _fleeThreshold = fleeThreshold;
        _decayPerSecond = decayPerSecond;
        _clickBump = clickBump;
    }

    public double Level { get; private set; }

    public bool IsFleeing => Level >= _fleeThreshold;

    public void Update(double deltaSeconds, double distanceToCursor)
    {
        Level = distanceToCursor < _proximityRadius
            ? Level + deltaSeconds
            : Math.Max(0, Level - (deltaSeconds * _decayPerSecond));
    }

    public void RegisterInteraction() => Level += _clickBump;

    public void Reset() => Level = 0;
}
