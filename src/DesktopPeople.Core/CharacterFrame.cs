namespace DesktopPeople.Core;

/// <summary>
/// Everything a renderer needs to draw one frame of the character, and nothing about how to
/// draw it — no <c>Graphics</c>, no Godot node, no colours.
/// <para>
/// This is the seam the whole rig-renderer effort rests on: the GDI+ renderer, a Godot
/// renderer, and eventually an avatar built from the user's own photograph all consume this
/// same record, so swapping how the character looks never means touching how it behaves.
/// </para>
/// </summary>
/// <param name="State">Which behaviour the character is in right now.</param>
/// <param name="Body">Where to draw it, in overlay coordinates. Not always the physics
/// position — easing into a hiding place slides the drawn body while the physics body has
/// already arrived.</param>
/// <param name="AnimationTime">Ever-increasing seconds, driving stride/bob cycles.</param>
/// <param name="Clicked">Briefly true after a click, for a startled reaction.</param>
/// <param name="CrouchAmount">0 standing, 1 fully crouched — landing impact and sitting.</param>
/// <param name="GazeTarget">Overlay point the eyes follow.</param>
/// <param name="ClimbWallDirection">Which side the climbed wall is on: 1 left, -1 right.</param>
/// <param name="HidePeekDirection">Which way the character peeks out: -1 left, 1 right.</param>
/// <param name="ClimbAmount">0..1 blend into the wall-clinging pose.</param>
/// <param name="HideAmount">0..1 blend into the tucked-around-the-corner pose.</param>
/// <param name="HidingWallBounds">Rectangle to clip the character against while hiding, if
/// hiding. The overlay always paints on top of every real window, so reading as "behind"
/// something is only achievable by not painting the covered part.</param>
public readonly record struct CharacterFrame(
    CharacterState State,
    RectD Body,
    double AnimationTime,
    bool Clicked,
    double CrouchAmount,
    Vec2 GazeTarget,
    int ClimbWallDirection,
    int HidePeekDirection,
    double ClimbAmount,
    double HideAmount,
    RectD? HidingWallBounds);
