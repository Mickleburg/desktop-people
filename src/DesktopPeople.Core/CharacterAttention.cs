namespace DesktopPeople.Core;

/// <summary>
/// Decides whether the character's gaze should track the cursor: always while the
/// user is actively interacting or the cursor is right next to it, otherwise only for
/// brief, occasional glances so it doesn't stare constantly at an idle cursor.
/// </summary>
public sealed class CharacterAttention
{
    private readonly double _proximityRadius;
    private readonly double _interactionWindowSeconds;
    private readonly double _glanceDuration;
    private readonly Random _random;
    private double? _nextGlanceAt;
    private double _glanceUntil;

    public CharacterAttention(
        double proximityRadius = 90,
        double interactionWindowSeconds = 4,
        double glanceDuration = 0.6,
        Random? random = null)
    {
        _proximityRadius = proximityRadius;
        _interactionWindowSeconds = interactionWindowSeconds;
        _glanceDuration = glanceDuration;
        _random = random ?? Random.Shared;
    }

    public bool ShouldTrackCursor(double now, double secondsSinceInteraction, double distanceToCursor)
    {
        if (secondsSinceInteraction < _interactionWindowSeconds || distanceToCursor < _proximityRadius)
        {
            return true;
        }

        _nextGlanceAt ??= ScheduleNextGlance(now);
        if (now >= _nextGlanceAt)
        {
            _glanceUntil = now + _glanceDuration;
            _nextGlanceAt = ScheduleNextGlance(now);
        }

        return now < _glanceUntil;
    }

    private double ScheduleNextGlance(double now) => now + 4 + (_random.NextDouble() * 5);
}
