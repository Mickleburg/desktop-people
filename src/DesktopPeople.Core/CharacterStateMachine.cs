namespace DesktopPeople.Core;

public sealed class CharacterStateMachine
{
    public CharacterState Current { get; private set; } = CharacterState.Spawn;

    public event Action<CharacterState, CharacterState, CharacterSignal>? StateChanged;

    public bool Send(CharacterSignal signal)
    {
        CharacterState next = Resolve(Current, signal);
        if (next == Current)
        {
            return false;
        }

        CharacterState previous = Current;
        Current = next;
        StateChanged?.Invoke(previous, next, signal);
        return true;
    }

    private static CharacterState Resolve(CharacterState current, CharacterSignal signal) =>
        (current, signal) switch
        {
            (_, CharacterSignal.Disable) => CharacterState.Disabled,
            (CharacterState.Disabled, CharacterSignal.Enable) => CharacterState.Fall,
            (CharacterState.Disabled, _) => CharacterState.Disabled,
            (_, CharacterSignal.Grabbed) => CharacterState.HeldByMouse,
            (CharacterState.HeldByMouse, CharacterSignal.Released) => CharacterState.Fall,
            (_, CharacterSignal.SupportLost) => CharacterState.Fall,
            (CharacterState.Spawn, CharacterSignal.Tick) => CharacterState.Fall,
            (CharacterState.Fall, CharacterSignal.Landed) => CharacterState.Idle,
            (CharacterState.Idle, CharacterSignal.WalkRequested) => CharacterState.Walk,
            (CharacterState.Idle, CharacterSignal.RunRequested) => CharacterState.Run,
            (CharacterState.Idle, CharacterSignal.SitRequested) => CharacterState.Sit,
            (CharacterState.Walk, CharacterSignal.StopRequested) => CharacterState.Idle,
            (CharacterState.Walk, CharacterSignal.RunRequested) => CharacterState.Run,
            (CharacterState.Walk, CharacterSignal.SitRequested) => CharacterState.Sit,
            (CharacterState.Run, CharacterSignal.StopRequested) => CharacterState.Idle,
            (CharacterState.Run, CharacterSignal.SitRequested) => CharacterState.Sit,
            (CharacterState.Sit, CharacterSignal.StandRequested) => CharacterState.Idle,
            _ => current,
        };
}

