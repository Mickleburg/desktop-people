using DesktopPeople.Core;
using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// The skin: it takes the joints <see cref="CharacterRig"/> has already worked out and puts a
/// body on them. It decides colour, thickness and outlines, never where a limb is.
/// <para>
/// That split is the point. Everything about the pose now lives in one tested place in
/// <c>DesktopPeople.Core</c> instead of being duplicated between two renderers, and an avatar
/// built from a photograph will hang on exactly these joints without the behaviour or the pose
/// maths changing at all.
/// </para>
/// </summary>
internal sealed class GodotCharacterRenderer
{
    private static readonly Color Ink = Color.Color8(41, 45, 62);
    private static readonly Color Accent = Color.Color8(111, 92, 255);
    private static readonly Color AccentWarm = Color.Color8(255, 125, 94);
    private static readonly Color Skin = Color.Color8(255, 222, 191);
    private static readonly Color Trouser = Color.Color8(63, 68, 94);
    private static readonly Color EyeWhite = Colors.White;

    /// <summary>Limbs are thin relative to the body on purpose. A limb's drawn width is its fill
    /// plus its outline on both sides, so an outline sized for the torso doubles a limb's
    /// apparent thickness — with the body outline the legs came out a quarter of the body wide
    /// each, wider together than the torso itself, and the arms read as dark bars with a sliver
    /// of skin down the middle rather than as arms.</summary>
    private const float ArmThickness = 0.085f;
    private const float LegThickness = 0.10f;
    private const float BodyOutline = 0.055f;
    private const float LimbOutline = 0.022f;

    public void Draw(CanvasItem canvas, CharacterFrame frame)
    {
        CharacterRigPose pose = CharacterRig.Solve(frame);
        var width = (float)frame.Body.Width;
        float outline = Mathf.Max(1.6f, width * BodyOutline);
        float limbOutline = Mathf.Max(1.2f, width * LimbOutline);
        Color bodyColor = pose.Clicked ? AccentWarm : Accent;

        // Everything goes under the torso, so where a limb is pinned on never shows: the torso
        // covers each shoulder and hip.
        if (pose.LegsVisible)
        {
            DrawLimb(canvas, pose.LegLeft, width * LegThickness, Trouser, limbOutline);
            DrawLimb(canvas, pose.LegRight, width * LegThickness, Trouser, limbOutline);
            DrawBlob(canvas, pose.LegLeft.Tip, (float)pose.FootRadius, limbOutline);
            DrawBlob(canvas, pose.LegRight.Tip, (float)pose.FootRadius, limbOutline);
        }

        // One arm can be on the near side of the body — reaching up a wall while climbing, or
        // braced against its edge while hiding — and behind the torso it would not be seen at
        // all. Which one that is comes from the rig, already settled for the whole climb: an
        // earlier version worked it out here by comparing the two hands' heights every frame,
        // and since both hands oscillate, the arm flicked in and out from behind the body.
        if (!pose.ArmLeftInFront)
        {
            DrawArm(canvas, pose.ArmLeft, pose.HandRadius, width, limbOutline);
        }

        if (pose.ArmRightVisible)
        {
            DrawArm(canvas, pose.ArmRight, pose.HandRadius, width, limbOutline);
        }

        DrawTorso(canvas, pose, bodyColor, outline);

        if (pose.ArmLeftInFront)
        {
            DrawArm(canvas, pose.ArmLeft, pose.HandRadius, width, limbOutline);
        }

        DrawHead(canvas, pose, outline);
    }

    private static void DrawArm(
        CanvasItem canvas, RigLimb arm, double handRadius, float width, float limbOutline)
    {
        DrawLimb(canvas, arm, width * ArmThickness, Skin, limbOutline);
        DrawBlob(canvas, arm.Tip, (float)handRadius, limbOutline);
    }

    /// <summary>A limb with real volume: two rounded segments through the elbow or knee, drawn as
    /// one outlined shape. The whole outline is laid down before any fill, so the joint between
    /// the two segments does not show as a seam across the middle of the limb.</summary>
    private static void DrawLimb(
        CanvasItem canvas, RigLimb limb, float thickness, Color fill, float outline)
    {
        Vector2 root = ToVector(limb.Root);
        Vector2 mid = ToVector(limb.Mid);
        Vector2 tip = ToVector(limb.Tip);
        float outer = thickness + (outline * 2);

        Stroke(canvas, root, mid, outer, Ink);
        Stroke(canvas, mid, tip, outer, Ink);
        canvas.DrawCircle(root, outer / 2, Ink);
        canvas.DrawCircle(mid, outer / 2, Ink);
        canvas.DrawCircle(tip, outer / 2, Ink);

        Stroke(canvas, root, mid, thickness, fill);
        Stroke(canvas, mid, tip, thickness, fill);
        canvas.DrawCircle(root, thickness / 2, fill);
        canvas.DrawCircle(mid, thickness / 2, fill);
        canvas.DrawCircle(tip, thickness / 2, fill);
    }

    /// <summary>A zero-length segment would make Godot draw nothing at all rather than a dot,
    /// which is exactly what a fully straightened or fully folded limb produces.</summary>
    private static void Stroke(CanvasItem canvas, Vector2 from, Vector2 to, float width, Color color)
    {
        if (from.DistanceSquaredTo(to) < 0.0001f)
        {
            return;
        }

        canvas.DrawLine(from, to, color, width, antialiased: true);
    }

    private static void DrawTorso(
        CanvasItem canvas, CharacterRigPose pose, Color fill, float outline)
    {
        var size = new Vector2((float)pose.TorsoWidth, (float)pose.TorsoHeight);
        Vector2[] polygon = RoundedRectPolygon(
            new Rect2(-size / 2, size), size.X * 0.3f);

        // Built around its own centre and then placed, so the hiding pose can rotate it about
        // the wall's edge without losing the rounded corners the standing pose has.
        var sin = (float)Math.Sin(pose.TorsoRotation);
        var cos = (float)Math.Cos(pose.TorsoRotation);
        Vector2 centre = ToVector(pose.TorsoCentre);
        for (int point = 0; point < polygon.Length; point++)
        {
            Vector2 local = polygon[point];
            polygon[point] = centre + new Vector2(
                (local.X * cos) - (local.Y * sin),
                (local.X * sin) + (local.Y * cos));
        }

        canvas.DrawColoredPolygon(polygon, fill);
        canvas.DrawPolyline([.. polygon, polygon[0]], Ink, outline, antialiased: true);
    }

    private static void DrawHead(CanvasItem canvas, CharacterRigPose pose, float outline)
    {
        Vector2 centre = ToVector(pose.HeadCentre);
        var radius = (float)pose.HeadRadius;
        canvas.DrawCircle(centre, radius, Skin);
        canvas.DrawCircle(centre, radius, Ink, filled: false, width: outline, antialiased: true);

        DrawEye(canvas, pose.EyeLeft, pose.PupilLeft, pose.EyeRadius, pose.PupilRadius);
        DrawEye(canvas, pose.EyeRight, pose.PupilRight, pose.EyeRadius, pose.PupilRadius);
    }

    private static void DrawEye(
        CanvasItem canvas, Vec2 socket, Vec2 pupil, double socketRadius, double pupilRadius)
    {
        canvas.DrawCircle(ToVector(socket), (float)socketRadius, EyeWhite);
        canvas.DrawCircle(ToVector(pupil), (float)pupilRadius, Colors.Black);
    }

    private static void DrawBlob(CanvasItem canvas, Vec2 centre, float radius, float outline)
    {
        if (radius <= 0.3f)
        {
            return;
        }

        Vector2 point = ToVector(centre);
        canvas.DrawCircle(point, radius, Skin);
        canvas.DrawCircle(point, radius, Ink, filled: false, width: outline, antialiased: true);
    }

    private static Vector2 ToVector(Vec2 value) => new((float)value.X, (float)value.Y);

    /// <summary>Godot has no rounded-rectangle primitive, so the torso is built as a polygon with
    /// arc-approximated corners.</summary>
    private static Vector2[] RoundedRectPolygon(Rect2 rect, float radius)
    {
        const int cornerSteps = 5;
        radius = Mathf.Min(radius, Mathf.Min(rect.Size.X, rect.Size.Y) / 2);
        var points = new List<Vector2>();

        void Corner(Vector2 centre, float startDegrees)
        {
            for (int step = 0; step <= cornerSteps; step++)
            {
                float angle = Mathf.DegToRad(startDegrees + (90f * step / cornerSteps));
                points.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        Corner(new Vector2(rect.End.X - radius, rect.Position.Y + radius), -90);
        Corner(new Vector2(rect.End.X - radius, rect.End.Y - radius), 0);
        Corner(new Vector2(rect.Position.X + radius, rect.End.Y - radius), 90);
        Corner(new Vector2(rect.Position.X + radius, rect.Position.Y + radius), 180);
        return [.. points];
    }
}
