using DesktopPeople.Core;

namespace DesktopPeople.Tests;

/// <summary>
/// The pose maths, now that it is out of the renderers and can be asserted on. Every case here
/// is a defect a person had to spot on screen and report, because until the rig existed there
/// was no way to ask where a hand was without painting one.
/// </summary>
internal static class CharacterRigTests
{
    public static TestCase[] All =>
    [
        new("rig stands with both feet on the ground line", FeetOnTheGroundLine),
        new("rig puts every gripping limb on the same wall line", GrippingLimbsShareTheWallLine),
        new("rig does not animate the grip during the climb cross-fade", NoGrabbingDuringCrossFade),
        new("rig keeps the pupils inside their sockets", PupilsStayInTheirSockets),
        new("rig bends the knee when crouching", CrouchBendsTheKnee),
        new("rig hides the far arm and both legs behind the wall", HidingKeepsOnlyThePeekingHalf),
        new("rig survives a body with no size", DegenerateBodyStaysFinite),
        new("rig keeps every joint near the body it was given", JointsStayNearTheBody),
        new("rig keeps the same arm in front for a whole climb", ClimbNeverSwapsTheFrontArm),
    ];

    /// <summary>Reported as the arm appearing and disappearing while climbing. Depth used to be
    /// decided by comparing the two hands' heights on each frame, and because both oscillate,
    /// their difference crosses zero about once a second — the near arm dropped behind the body
    /// and came back out, over and over. Swept across several full cycles of the grab.
    /// </summary>
    private static void ClimbNeverSwapsTheFrontArm()
    {
        CharacterFrame climbing =
            Frame(CharacterState.Climb) with { ClimbAmount = 1, ClimbWallDirection = 1 };

        for (int step = 0; step < 240; step++)
        {
            CharacterRigPose pose = CharacterRig.Solve(
                climbing with { AnimationTime = step * 0.02 });
            AssertEx.True(pose.ArmLeftInFront);
        }

        // Standing still, neither arm crosses the body, so nothing is lifted out in front.
        AssertEx.False(CharacterRig.Solve(Frame(CharacterState.Idle)).ArmLeftInFront);
        AssertEx.False(CharacterRig.Solve(Frame(CharacterState.Walk)).ArmLeftInFront);
    }

    /// <summary>The character must be drawn where the simulation thinks it is. A joint that
    /// wanders far outside <c>Body</c> means the drawn character and its hit box, its collision
    /// footprint and its hiding clip have come apart — which on screen reads as a character
    /// standing beside itself. The only deliberate overreach is a limb reaching for a wall, a
    /// few percent of the body width past its edge.</summary>
    private static void JointsStayNearTheBody()
    {
        foreach (CharacterState state in Enum.GetValues<CharacterState>())
        {
            CharacterFrame frame = Frame(state) with
            {
                AnimationTime = 1.7,
                CrouchAmount = 1,
                ClimbAmount = 1,
                HideAmount = 1,
                ClimbWallDirection = 1,
                HidePeekDirection = 1,
            };

            CharacterRigPose pose = CharacterRig.Solve(frame);
            RectD allowed = Body.Inflate(Body.Width * 0.2, Body.Height * 0.2);

            foreach (Vec2 joint in Joints(pose))
            {
                AssertEx.True(allowed.Contains(joint));
            }
        }
    }

    private static IEnumerable<Vec2> Joints(CharacterRigPose pose)
    {
        yield return pose.HeadCentre;
        yield return pose.TorsoCentre;
        yield return pose.ArmLeft.Root;
        yield return pose.ArmLeft.Tip;
        if (pose.ArmRightVisible)
        {
            yield return pose.ArmRight.Root;
            yield return pose.ArmRight.Tip;
        }

        if (pose.LegsVisible)
        {
            yield return pose.LegLeft.Root;
            yield return pose.LegLeft.Tip;
            yield return pose.LegRight.Root;
            yield return pose.LegRight.Tip;
        }
    }

    private static void FeetOnTheGroundLine()
    {
        CharacterRigPose pose = CharacterRig.Solve(Frame(CharacterState.Idle));

        // The renderer draws the character inside Body; feet resting anywhere else is what makes
        // it look like it is hovering above a window rather than standing on one.
        double ground = Body.Y + Body.Height - (Body.Height * 0.05);
        AssertEx.True(Math.Abs(pose.LegLeft.Tip.Y - ground) < 0.001);
        AssertEx.True(Math.Abs(pose.LegRight.Tip.Y - ground) < 0.001);
        AssertEx.True(pose.LegsVisible);
    }

    private static void GrippingLimbsShareTheWallLine()
    {
        CharacterRigPose pose = CharacterRig.Solve(
            Frame(CharacterState.Climb) with { ClimbAmount = 1, ClimbWallDirection = 1 });

        // Reported as one leg overshooting the wall while the other never reached it: each limb
        // used to work out its own reach from its own joint.
        double wall = Body.X + Body.Width + (Body.Width * 0.03);
        AssertEx.True(Math.Abs(pose.ArmLeft.Tip.X - wall) < 0.001);
        AssertEx.True(Math.Abs(pose.ArmRight.Tip.X - wall) < 0.001);
        AssertEx.True(Math.Abs(pose.LegLeft.Tip.X - wall) < 0.001);
        AssertEx.True(Math.Abs(pose.LegRight.Tip.X - wall) < 0.001);
    }

    /// <summary>The "arms pawing at the air" defect, reported twice and mis-diagnosed once. While
    /// the pose is still blending between walking and gripping, the hands are somewhere in
    /// mid-air, and running the hand-over-hand cycle there is what the user saw. The check is
    /// that a half-blended climb does not move at all as time passes.</summary>
    private static void NoGrabbingDuringCrossFade()
    {
        CharacterFrame midBlend =
            Frame(CharacterState.Climb) with { ClimbAmount = 0.5, ClimbWallDirection = 1 };

        CharacterRigPose first = CharacterRig.Solve(midBlend with { AnimationTime = 0.0 });
        CharacterRigPose second = CharacterRig.Solve(midBlend with { AnimationTime = 0.37 });

        AssertEx.Equal(first.ArmLeft.Tip, second.ArmLeft.Tip);
        AssertEx.Equal(first.ArmRight.Tip, second.ArmRight.Tip);
        AssertEx.Equal(first.LegLeft.Tip, second.LegLeft.Tip);
        AssertEx.Equal(first.LegRight.Tip, second.LegRight.Tip);

        // ...and that a fully engaged grip does move, so the guard above is not simply frozen.
        CharacterFrame gripped = midBlend with { ClimbAmount = 1 };
        AssertEx.True(
            CharacterRig.Solve(gripped with { AnimationTime = 0.0 }).ArmLeft.Tip !=
            CharacterRig.Solve(gripped with { AnimationTime = 0.37 }).ArmLeft.Tip);
    }

    private static void PupilsStayInTheirSockets()
    {
        CharacterRigPose pose = CharacterRig.Solve(
            Frame(CharacterState.Idle) with { GazeTarget = new Vec2(50_000, -50_000) });

        AssertEx.True((pose.PupilLeft - pose.EyeLeft).Length <= pose.EyeRadius);
        AssertEx.True((pose.PupilRight - pose.EyeRight).Length <= pose.EyeRadius);
    }

    private static void CrouchBendsTheKnee()
    {
        CharacterRigPose straight = CharacterRig.Solve(Frame(CharacterState.Idle));
        CharacterRigPose crouched = CharacterRig.Solve(
            Frame(CharacterState.Idle) with { CrouchAmount = 1 });

        // A crouch used to be a barely visible vertical shift; the knee has to leave the
        // hip-to-foot line for it to read as bent legs at all.
        AssertEx.True(DistanceFromHipToFootLine(straight.LegLeft) < 0.001);
        AssertEx.True(DistanceFromHipToFootLine(crouched.LegLeft) > Body.Width * 0.1);
    }

    private static void HidingKeepsOnlyThePeekingHalf()
    {
        CharacterRigPose pose = CharacterRig.Solve(
            Frame(CharacterState.Hide) with { HideAmount = 1, HidePeekDirection = 1 });

        AssertEx.False(pose.LegsVisible);
        AssertEx.False(pose.ArmRightVisible);

        // The single rotation is the whole trick: it swings the head one way and the torso the
        // other, so a straight vertical clip separates them. Checked on both sides, because a
        // pose that leaned the same way whichever wall it hid behind would pass a one-sided
        // check and still put the character's head through the window.
        AssertEx.True(Math.Abs(pose.TorsoRotation) > 0.5);
        AssertEx.True(pose.HeadCentre.X > pose.TorsoCentre.X);

        CharacterRigPose mirrored = CharacterRig.Solve(
            Frame(CharacterState.Hide) with { HideAmount = 1, HidePeekDirection = -1 });
        AssertEx.True(mirrored.HeadCentre.X < mirrored.TorsoCentre.X);
    }

    private static void DegenerateBodyStaysFinite()
    {
        // The simulation repairs a corrupted body, but the rig must not be the thing that turns
        // a bad frame into a renderer crash before that repair happens.
        CharacterRigPose pose = CharacterRig.Solve(
            Frame(CharacterState.Idle) with { Body = new RectD(0, 0, 0, 0) });

        AssertEx.True(double.IsFinite(pose.HeadCentre.X) && double.IsFinite(pose.HeadCentre.Y));
        AssertEx.True(double.IsFinite(pose.PupilLeft.X) && double.IsFinite(pose.PupilLeft.Y));
        AssertEx.True(double.IsFinite(pose.LegLeft.Tip.X) && double.IsFinite(pose.LegLeft.Tip.Y));
    }

    private static double DistanceFromHipToFootLine(RigLimb leg)
    {
        Vec2 span = leg.Tip - leg.Root;
        double length = span.Length;
        if (length < 0.001)
        {
            return 0;
        }

        Vec2 toKnee = leg.Mid - leg.Root;
        return Math.Abs((span.X * toKnee.Y) - (span.Y * toKnee.X)) / length;
    }

    private static RectD Body => new(400, 500, 60, 120);

    private static CharacterFrame Frame(CharacterState state) => new(
        state,
        Body,
        AnimationTime: 0,
        Clicked: false,
        CrouchAmount: 0,
        GazeTarget: new Vec2(430, 520),
        ClimbWallDirection: 0,
        HidePeekDirection: 0,
        ClimbAmount: 0,
        HideAmount: 0,
        HidingWallBounds: null);
}
