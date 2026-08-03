namespace DesktopPeople.Core;

/// <summary>Read-only view of the simulation's internals for the developer overlay only.
/// Gathered as a single snapshot so debug tooling never becomes an argument for making the
/// simulation's own state publicly writable.</summary>
public readonly record struct CharacterDiagnostics(
    CharacterState State,
    Vec2 Velocity,
    RectD Body,
    (double Left, double Right) FootInterval,
    string? CurrentPlatformId,
    int PlatformCount,
    bool IsAttached,
    double? AttachmentFootCenterX,
    string BehaviorIntensity,
    double CursorEnergy,
    double HarassmentLevel,
    bool IsFleeing,
    bool IsClimbing,
    string? HidingPlatformId,
    double CharacterScale);
