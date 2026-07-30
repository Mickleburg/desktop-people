namespace DesktopPeople.Core;

public enum CharacterState
{
    Spawn,
    Idle,
    Walk,
    Fall,
    HeldByMouse,
    Disabled,
}

public enum CharacterSignal
{
    Tick,
    Landed,
    WalkRequested,
    StopRequested,
    Grabbed,
    Released,
    SupportLost,
    Disable,
    Enable,
}

