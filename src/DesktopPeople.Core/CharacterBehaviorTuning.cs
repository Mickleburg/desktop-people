namespace DesktopPeople.Core;

public sealed record CharacterBehaviorTuning(
    double IdleDelay,
    double WalkDuration,
    double RunDuration,
    double SitDuration,
    double SitChance,
    double RunChance,
    double JumpChance = 0,
    double ClimbChance = 0,
    double HideChance = 0,
    double GrabChance = 0,
    double HideDuration = 3.5)
{
    public static CharacterBehaviorTuning ForIntensity(string intensity) =>
        ForLevel(IntensityLevel(intensity));

    /// <summary>
    /// Blends the manually chosen intensity with how energetically the user has been
    /// moving the pointer lately (0 = still, 1 = very active): active cursor motion
    /// nudges the character toward the next livelier profile, a still cursor toward
    /// the calmer one, without ever leaving the neighborhood of the manual setting.
    /// </summary>
    public static CharacterBehaviorTuning ForEnergy(string baseIntensity, double cursorEnergy)
    {
        double level = IntensityLevel(baseIntensity) + ((Math.Clamp(cursorEnergy, 0, 1) - 0.5) * 1.4);
        level = Math.Clamp(level, 0, 2);
        int lower = (int)Math.Floor(level);
        int upper = Math.Min(lower + 1, 2);
        return Lerp(ForLevel(lower), ForLevel(upper), level - lower);
    }

    public CharacterSignal PickAutonomousTransition(double roll)
    {
        double sitThreshold = JumpChance + SitChance;
        double runThreshold = sitThreshold + RunChance;
        if (roll < JumpChance)
        {
            return CharacterSignal.JumpRequested;
        }

        if (roll < sitThreshold)
        {
            return CharacterSignal.SitRequested;
        }

        if (roll < runThreshold)
        {
            return CharacterSignal.RunRequested;
        }

        return CharacterSignal.WalkRequested;
    }

    private static int IntensityLevel(string intensity) => intensity switch
    {
        "calm" => 0,
        "active" => 2,
        _ => 1,
    };

    private static CharacterBehaviorTuning ForLevel(int level) => level switch
    {
        0 => new CharacterBehaviorTuning(
            3.6, 3.0, 1.6, 4.5, 0.45, 0.05,
            JumpChance: 0.02, ClimbChance: 0.10, HideChance: 0.30, GrabChance: 0.35, HideDuration: 5.0),
        2 => new CharacterBehaviorTuning(
            1.4, 4.5, 3.0, 1.8, 0.10, 0.45,
            JumpChance: 0.12, ClimbChance: 0.32, HideChance: 0.08, GrabChance: 0.55, HideDuration: 2.0),
        _ => new CharacterBehaviorTuning(
            2.4, 3.8, 2.2, 3.2, 0.25, 0.20,
            JumpChance: 0.05, ClimbChance: 0.20, HideChance: 0.18, GrabChance: 0.45, HideDuration: 3.5),
    };

    private static CharacterBehaviorTuning Lerp(CharacterBehaviorTuning a, CharacterBehaviorTuning b, double t) =>
        new(
            Lerp(a.IdleDelay, b.IdleDelay, t),
            Lerp(a.WalkDuration, b.WalkDuration, t),
            Lerp(a.RunDuration, b.RunDuration, t),
            Lerp(a.SitDuration, b.SitDuration, t),
            Lerp(a.SitChance, b.SitChance, t),
            Lerp(a.RunChance, b.RunChance, t),
            Lerp(a.JumpChance, b.JumpChance, t),
            Lerp(a.ClimbChance, b.ClimbChance, t),
            Lerp(a.HideChance, b.HideChance, t),
            Lerp(a.GrabChance, b.GrabChance, t),
            Lerp(a.HideDuration, b.HideDuration, t));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
