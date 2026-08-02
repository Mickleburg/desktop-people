namespace DesktopPeople.Core;

public sealed record CharacterBehaviorTuning(
    double IdleDelay,
    double WalkDuration,
    double RunDuration,
    double SitDuration,
    double SitChance,
    double RunChance)
{
    public static CharacterBehaviorTuning ForIntensity(string intensity) => intensity switch
    {
        "calm" => new CharacterBehaviorTuning(3.6, 3.0, 1.6, 4.5, 0.45, 0.05),
        "active" => new CharacterBehaviorTuning(1.4, 4.5, 3.0, 1.8, 0.10, 0.45),
        _ => new CharacterBehaviorTuning(2.4, 3.8, 2.2, 3.2, 0.25, 0.20),
    };

    public CharacterSignal PickAutonomousTransition(double roll)
    {
        if (roll < SitChance)
        {
            return CharacterSignal.SitRequested;
        }

        if (roll < SitChance + RunChance)
        {
            return CharacterSignal.RunRequested;
        }

        return CharacterSignal.WalkRequested;
    }
}
