namespace DesktopPeople.Core.Platforms;

public sealed class CharacterPlatformController
{
    private readonly CharacterPlatformAttachment _attachment;

    public CharacterPlatformController(CharacterPlatformAttachment attachment)
    {
        _attachment = attachment;
    }

    public bool TryFollow(
        PlatformSnapshot snapshot,
        CharacterPhysics physics,
        CharacterStateMachine stateMachine,
        out DesktopPlatform? platform,
        out string? lostPlatformId)
    {
        platform = null;
        lostPlatformId = null;
        if (!_attachment.IsAttached)
        {
            return false;
        }

        platform = snapshot.Platforms.FirstOrDefault(
            candidate => candidate.Id == _attachment.PlatformId);
        if (platform is not null &&
            _attachment.TryFollow(platform, physics.Bounds, out Vec2 targetPosition))
        {
            physics.SetPosition(targetPosition);
            return true;
        }

        lostPlatformId = _attachment.PlatformId;
        _attachment.Detach();
        platform = null;
        stateMachine.Send(CharacterSignal.SupportLost);
        return false;
    }
}
