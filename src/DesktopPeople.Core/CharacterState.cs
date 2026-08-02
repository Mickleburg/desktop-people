namespace DesktopPeople.Core;

public enum CharacterState
{
    Spawn,
    Idle,
    Walk,
    Run,
    Sit,
    Climb,
    Hide,
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
    ClimbRequested,
    JumpRequested,
    HideRequested,
    StopRequested,
    Grabbed,
    Released,
    SupportLost,
    Disable,
    Enable,
}

