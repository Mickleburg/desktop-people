namespace DesktopPeople.Core;

/// <summary>One limb of the rig: the joint it hangs from, its middle joint (elbow or knee) and
/// its tip (hand or foot). Straight limbs are expressed with the middle joint exactly halfway,
/// so a renderer never needs to ask whether this limb happens to be bent.</summary>
public readonly record struct RigLimb(Vec2 Root, Vec2 Mid, Vec2 Tip);

/// <summary>
/// Where every joint of the character is this frame, in overlay coordinates.
/// <para>
/// This is the skeleton, not the skin: it says where the head, torso and limbs are, never what
/// colour they are or how their edges are drawn. That split is what lets the current drawn
/// character and a later avatar built from a photograph share one body — the photograph changes
/// what is stretched over these joints, not where the joints go.
/// </para>
/// <para>
/// It also moves the pose maths out of a renderer and into something a test can hold: until now
/// every one of these numbers only existed inside a paint call, which is why defects in them
/// (feet not reaching the wall, arms clawing at air, a head floating off the shoulders) could
/// only ever be found by a person looking at the screen.
/// </para>
/// </summary>
public readonly record struct CharacterRigPose(
    Vec2 HeadCentre,
    double HeadRadius,
    Vec2 EyeLeft,
    Vec2 EyeRight,
    double EyeRadius,
    Vec2 PupilLeft,
    Vec2 PupilRight,
    double PupilRadius,
    Vec2 TorsoCentre,
    double TorsoWidth,
    double TorsoHeight,
    double TorsoRotation,
    RigLimb ArmLeft,
    RigLimb ArmRight,
    RigLimb LegLeft,
    RigLimb LegRight,
    double HandRadius,
    double FootRadius,
    bool ArmRightVisible,

    /// <summary>Whether the left arm is on the near side of the body — reaching across the chest
    /// for a wall while climbing, or braced against a wall's edge while hiding.
    /// <para>
    /// Depth belongs here rather than in a renderer, because it has to be stable. Deciding it by
    /// comparing the two hands' heights every frame looks equivalent and is not: while climbing
    /// both hands oscillate, the difference between them crosses zero about once a second, and
    /// the arm then jumps behind the body and back out again — which is what "the arm keeps
    /// appearing and disappearing" was.
    /// </para>
    /// </summary>
    bool ArmLeftInFront,
    bool LegsVisible,
    bool Clicked);

/// <summary>
/// Turns a <see cref="CharacterFrame"/> into the joint positions a renderer draws over.
/// <para>
/// Every proportion here was previously duplicated between the GDI+ and Godot renderers, which
/// is how a fix for the climbing arms once had to be written twice. There is one copy now.
/// </para>
/// </summary>
public static class CharacterRig
{
    /// <summary>How far the climbing pose must have blended in before the character counts as
    /// gripping the wall rather than still reaching for it. Running the alternating grab during
    /// the cross-fade is exactly what reads as pawing at the air at each end of a climb.</summary>
    private const double GripEngagedAmount = 0.95;

    public static CharacterRigPose Solve(CharacterFrame frame)
    {
        double x = frame.Body.X;
        double y = frame.Body.Y;
        double width = frame.Body.Width;
        double height = frame.Body.Height;

        if (frame.State == CharacterState.Hide)
        {
            return SolveHide(frame, x, y, width, height);
        }

        bool running = frame.State == CharacterState.Run;
        bool climbing = frame.State == CharacterState.Climb;
        bool locomoting = frame.State is CharacterState.Walk or CharacterState.Run;
        double climbAmount = Math.Clamp(frame.ClimbAmount, 0, 1);
        double cadence = running ? 15 : (climbing ? 7 : 11);
        double bob = locomoting
            ? Math.Sin(frame.AnimationTime * cadence) * (running ? width * 0.096 : width * 0.053)
            : 0;
        double stride = locomoting
            ? Math.Sin(frame.AnimationTime * cadence) * (running ? width * 0.43 : width * 0.28)
            : 0;

        bool gripping = climbing && climbAmount >= GripEngagedAmount;
        double climbReach = gripping ? Math.Sin(frame.AnimationTime * cadence) * height * 0.07 : 0;
        double climbLegCycle = gripping ? Math.Sin(frame.AnimationTime * cadence) : 0;

        // One shared wall line for both hands and both feet. Deriving each limb's reach from its
        // own joint made one leg overshoot the wall while the other never touched it.
        double wallLineX = frame.ClimbWallDirection > 0
            ? x + width + (width * 0.03)
            : x - (width * 0.03);

        double crouch = Math.Clamp(frame.CrouchAmount, 0, 1);
        double crouchDrop = crouch * height * 0.26;
        double stretch = crouch * width * 0.1;
        double climbLean = climbAmount * frame.ClimbWallDirection * width * 0.16;
        double legBottom = y + height - (height * 0.05);

        double headSize = width * 0.52;
        double headX = x + ((width - headSize) / 2) + climbLean;
        double headY = y + (height * 0.017) + bob + crouchDrop;
        var headCentre = new Vec2(headX + (headSize / 2), headY + (headSize / 2));

        double eyeSize = headSize * 0.19;
        double eyeCentreY = headY + (headSize * 0.44) + (eyeSize / 2);
        var eyeLeft = new Vec2(headX + (headSize * 0.26) + (eyeSize / 2), eyeCentreY);
        var eyeRight = new Vec2(headX + (headSize * 0.74) - (eyeSize / 2), eyeCentreY);

        double torsoTop = headY + (headSize * 0.96);
        double torsoHeight = height * 0.34;
        double torsoWidth = (width * 0.42) + stretch;
        double torsoLeft = x + (width * 0.29) - (stretch / 2) + climbLean;
        double torsoRight = torsoLeft + torsoWidth;

        double shoulderY = torsoTop + (height * 0.08);
        double hipY = torsoTop + torsoHeight - (height * 0.01);
        var shoulderLeft = new Vec2(torsoLeft + (width * 0.04), shoulderY);
        var shoulderRight = new Vec2(torsoRight - (width * 0.04), shoulderY);

        Vec2 handLeft;
        Vec2 handRight;
        double handRadius = 0;

        if (frame.State == CharacterState.Fall)
        {
            // Arms thrown up, which is the whole visual cue that the character is falling
            // rather than standing still in mid-air.
            handLeft = new Vec2(x + (width * 0.05), shoulderY - (height * 0.12));
            handRight = new Vec2(x + width - (width * 0.05), shoulderY - (height * 0.12));
        }
        else
        {
            handLeft = new Vec2(x + (width * 0.11) - (stride * 0.25), hipY - (height * 0.02));
            handRight = new Vec2(x + width - (width * 0.11) + (stride * 0.25), hipY - (height * 0.02));

            if (climbAmount > 0.001)
            {
                // The two hands reach different heights on the same wall line, and the reach
                // alternates, so the character reads as pulling itself up hand over hand.
                var gripLeft = new Vec2(wallLineX, shoulderY - (height * 0.05) - climbReach);
                var gripRight = new Vec2(wallLineX, shoulderY + (height * 0.07) + climbReach);
                handLeft = Blend(handLeft, gripLeft, climbAmount);
                handRight = Blend(handRight, gripRight, climbAmount);
                handRadius = width * 0.08 * climbAmount;
            }
        }

        RigLimb legLeft = SolveLeg(
            new Vec2(torsoLeft + (width * 0.06), hipY),
            x + (width * 0.27) + stride,
            legBottom, width, crouch, mirrored: false, climbAmount, wallLineX, climbLegCycle);
        RigLimb legRight = SolveLeg(
            new Vec2(torsoRight - (width * 0.06), hipY),
            x + width - (width * 0.27) - stride,
            legBottom, width, crouch, mirrored: true, climbAmount, wallLineX, -climbLegCycle);

        double footRadius = width * 0.065 * climbAmount;

        return new CharacterRigPose(
            headCentre,
            headSize / 2,
            eyeLeft,
            eyeRight,
            eyeSize / 2,
            Pupil(eyeLeft, eyeSize, frame.GazeTarget),
            Pupil(eyeRight, eyeSize, frame.GazeTarget),
            eyeSize * 0.275,
            new Vec2(torsoLeft + (torsoWidth / 2), torsoTop + (torsoHeight / 2)),
            torsoWidth,
            torsoHeight,
            TorsoRotation: 0,
            Straight(shoulderLeft, handLeft),
            Straight(shoulderRight, handRight),
            legLeft,
            legRight,
            handRadius,
            footRadius > 0.3 ? footRadius : 0,
            ArmRightVisible: true,

            // The climbing grip is built with the left arm anchored above the shoulder and the
            // right below it, so the left is the one reaching up the wall for the whole climb.
            // Fixed for the climb rather than re-decided per frame, on purpose — see the field.
            ArmLeftInFront: climbAmount > 0.001,
            LegsVisible: true,
            frame.Clicked);
    }

    /// <summary>The whole character rigidly rotated about a pivot on the wall's own edge at
    /// shoulder height. One rotation swings whatever is above the pivot one way and whatever is
    /// below it the other, so a straight vertical clip cuts the silhouette into a head that peeks
    /// out and a torso that stays hidden — rather than a barely-offset standing pose.</summary>
    private static CharacterRigPose SolveHide(
        CharacterFrame frame, double x, double y, double width, double height)
    {
        double hideEase = Math.Clamp(frame.HideAmount, 0, 1);
        double pivotX = x + (width / 2);
        double pivotY = y + (height * 0.34);
        double lean = frame.HidePeekDirection * (Math.PI / 4) * hideEase;
        double sin = Math.Sin(lean);
        double cos = Math.Cos(lean);

        Vec2 FromPivot(double dx, double dy) => new(
            pivotX + (dx * cos) - (dy * sin),
            pivotY + (dx * sin) + (dy * cos));

        double headSize = width * 0.52;

        // Head-to-pivot distance follows the head's own size, mirroring how the head meets the
        // torso in the standing pose. Tying it to overall height instead once left a gap that
        // read as a head floating free of the shoulders.
        double headOffset = headSize * 0.46;
        Vec2 headCentre = FromPivot(0, -headOffset);

        double torsoWidth = width * 0.42;
        double torsoHeight = height * 0.34;

        double eyeSize = headSize * 0.19;
        double eyeSpread = headSize * 0.145;
        double eyeDrop = headSize * 0.02;
        Vec2 eyeLeft = FromPivot(-eyeSpread, -headOffset + eyeDrop);
        Vec2 eyeRight = FromPivot(eyeSpread, -headOffset + eyeDrop);

        // A hand braced on the wall's edge: it stays on the wall plane rather than turning with
        // the shoulder behind it.
        Vec2 shoulder = FromPivot(0, height * 0.04);
        var hand = new Vec2(pivotX, pivotY + (height * 0.12));

        return new CharacterRigPose(
            headCentre,
            headSize / 2,
            eyeLeft,
            eyeRight,
            eyeSize / 2,
            Pupil(eyeLeft, eyeSize, frame.GazeTarget),
            Pupil(eyeRight, eyeSize, frame.GazeTarget),
            eyeSize * 0.275,
            FromPivot(0, torsoHeight / 2),
            torsoWidth,
            torsoHeight,
            lean,
            Straight(shoulder, hand),
            default,
            default,
            default,
            width * 0.08,
            FootRadius: 0,

            // Everything below the shoulder is behind the wall while hiding; drawing the far arm
            // and the legs would push limbs out past the clip on the visible side.
            ArmRightVisible: false,

            // The one remaining arm braces on the near side of the wall's edge.
            ArmLeftInFront: true,
            LegsVisible: false,
            frame.Clicked);
    }

    private static RigLimb SolveLeg(
        Vec2 hip,
        double footX,
        double footBottom,
        double width,
        double crouch,
        bool mirrored,
        double climbAmount,
        double wallLineX,
        double climbPhase)
    {
        double legSpan = footBottom - hip.Y;
        double standKneeX = hip.X + ((footX - hip.X) * 0.5);
        double standKneeY = hip.Y + (legSpan * 0.5);
        double standFootX = footX;

        if (crouch >= 0.02)
        {
            // A real bent knee rather than a vertical shift: the knee swings out to the side and
            // the foot tucks back under the hip.
            double direction = mirrored ? 1 : -1;
            standKneeX = hip.X + (direction * width * 0.36 * crouch);
            standKneeY = hip.Y + (legSpan * 0.34 * crouch);
            standFootX = footX + ((hip.X + (direction * width * 0.1)) - footX) * crouch;
        }

        var standKnee = new Vec2(standKneeX, standKneeY);
        var standFoot = new Vec2(standFootX, footBottom);
        if (climbAmount <= 0.001)
        {
            return new RigLimb(hip, standKnee, standFoot);
        }

        var climbKnee = new Vec2(
            hip.X + ((wallLineX - hip.X) * 0.55),
            hip.Y + (legSpan * 0.3) + (climbPhase * legSpan * 0.08));
        var climbFoot = new Vec2(
            wallLineX,
            footBottom - (legSpan * 0.1) + (climbPhase * legSpan * 0.1));

        return new RigLimb(
            hip,
            Blend(standKnee, climbKnee, climbAmount),
            Blend(standFoot, climbFoot, climbAmount));
    }

    /// <summary>The pupil, tracking the pointer but never leaving its socket.</summary>
    private static Vec2 Pupil(Vec2 socket, double socketSize, Vec2 gazeTarget)
    {
        double range = socketSize * 0.24;
        var toTarget = new Vec2(gazeTarget.X - socket.X, gazeTarget.Y - socket.Y);
        double distance = toTarget.Length;
        return distance > 0.01
            ? socket + (toTarget * (Math.Min(range, distance) / distance))
            : socket;
    }

    private static RigLimb Straight(Vec2 root, Vec2 tip) =>
        new(root, new Vec2((root.X + tip.X) / 2, (root.Y + tip.Y) / 2), tip);

    private static Vec2 Blend(Vec2 from, Vec2 to, double amount) => new(
        from.X + ((to.X - from.X) * amount),
        from.Y + ((to.Y - from.Y) * amount));
}
