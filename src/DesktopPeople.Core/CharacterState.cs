namespace DesktopPeople.Core;

public enum CharacterState
{
    Spawn,
    Idle,
    Walk,
    Run,
    Sit,
    Fall,
    HeldByMouse,
    Disabled,
}

public enum CharacterSignal
{
    Tick,
    Landed,
    WalkRequested,
    RunRequested,
    SitRequested,
    StandRequested,
    StopRequested,
    Grabbed,
    Released,
    SupportLost,
    Disable,
    Enable,
}

