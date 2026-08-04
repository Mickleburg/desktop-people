using DesktopPeople.Core;
using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// Draws a <see cref="CharacterFrame"/> with Godot's canvas API. A direct port of the GDI+
/// renderer's proportions and poses, so the character reads identically in both hosts — the
/// point of <see cref="CharacterFrame"/> is that only this file changes when the artwork
/// changes, never the behaviour behind it.
/// </summary>
internal sealed class GodotCharacterRenderer
{
    private static readonly Color Ink = Color.Color8(41, 45, 62);
    private static readonly Color Accent = Color.Color8(111, 92, 255);
    private static readonly Color AccentWarm = Color.Color8(255, 125, 94);
    private static readonly Color Skin = Color.Color8(255, 222, 191);
    private static readonly Color EyeWhite = Colors.White;

    /// <summary>How far the climbing pose must have blended in before the character counts as
    /// gripping the wall rather than still reaching for it.</summary>
    private const float GripEngagedAmount = 0.95f;

    public void Draw(CanvasItem canvas, CharacterFrame frame)
    {
        var x = (float)frame.Body.X;
        var y = (float)frame.Body.Y;
        var width = (float)frame.Body.Width;
        var height = (float)frame.Body.Height;

        bool running = frame.State == CharacterState.Run;
        bool climbing = frame.State == CharacterState.Climb;
        bool locomoting = frame.State is CharacterState.Walk or CharacterState.Run;
        float climbAmount = Mathf.Clamp((float)frame.ClimbAmount, 0, 1);
        double cadence = running ? 15 : (climbing ? 7 : 11);
        float bobAmplitude = running ? width * 0.096f : width * 0.053f;
        float strideAmplitude = running ? width * 0.43f : width * 0.28f;
        float bob = locomoting ? (float)(Mathf.Sin(frame.AnimationTime * cadence) * bobAmplitude) : 0;
        float stride = locomoting ? (float)(Mathf.Sin(frame.AnimationTime * cadence) * strideAmplitude) : 0;

        // The alternating grab plays only while the character is actually gripping the wall:
        // in the Climb state AND with the pose fully blended in. Gating on the state alone was
        // not enough — for the whole cross-fade at each end of a climb the arms sit somewhere
        // between the walking swing and the grip, and running the climbing oscillation there is
        // exactly what reads as pawing at the air at the start and the end of every climb.
        // ClimbAmount also carries the wall contact, so this stops the cycle as the character
        // transfers sideways onto the ledge at the top too.
        bool gripping = climbing && climbAmount >= GripEngagedAmount;
        float climbReach = gripping
            ? (float)(Mathf.Sin(frame.AnimationTime * cadence) * height * 0.07)
            : 0;
        float climbLegCycle = gripping ? (float)Mathf.Sin(frame.AnimationTime * cadence) : 0;

        // One shared wall line for both hands and both feet, so all four limbs reach the same
        // surface instead of each approximating it and drifting apart.
        float climbWallLineX = frame.ClimbWallDirection > 0
            ? x + width + (width * 0.03f)
            : x - (width * 0.03f);

        float crouch = Mathf.Clamp((float)frame.CrouchAmount, 0, 1);
        float outlineWidth = Mathf.Max(1.6f, width * 0.055f);
        Color bodyColor = frame.Clicked ? AccentWarm : Accent;
        float legBottom = y + height - (height * 0.05f);

        if (frame.State == CharacterState.Hide)
        {
            DrawHidePose(canvas, frame, x, y, width, height, outlineWidth, bodyColor);
            return;
        }

        float crouchDrop = crouch * height * 0.26f;
        float stretch = crouch * width * 0.1f;
        float climbLean = climbAmount * frame.ClimbWallDirection * width * 0.16f;

        float headSize = width * 0.52f;
        float headX = x + ((width - headSize) / 2) + climbLean;
        float headY = y + (height * 0.017f) + bob + crouchDrop;
        var headCentre = new Vector2(headX + (headSize / 2), headY + (headSize / 2));
        canvas.DrawCircle(headCentre, headSize / 2, Skin);
        canvas.DrawCircle(headCentre, headSize / 2, Ink, filled: false, width: outlineWidth, antialiased: true);

        float eyeSize = headSize * 0.19f;
        float eyeY = headY + (headSize * 0.44f);
        DrawEye(canvas, headX + (headSize * 0.26f), eyeY, eyeSize, frame.GazeTarget);
        DrawEye(canvas, headX + (headSize * 0.74f) - eyeSize, eyeY, eyeSize, frame.GazeTarget);

        float torsoTop = headY + (headSize * 0.96f);
        float torsoHeight = height * 0.34f;
        var torso = new Rect2(
            x + (width * 0.29f) - (stretch / 2) + climbLean,
            torsoTop,
            (width * 0.42f) + stretch,
            torsoHeight);
        DrawRoundedRect(canvas, torso, torso.Size.X * 0.3f, bodyColor, outlineWidth);

        float shoulderY = torsoTop + (height * 0.08f);
        float hipY = torso.End.Y - (height * 0.01f);

        if (frame.State == CharacterState.Fall)
        {
            canvas.DrawLine(
                new Vector2(torso.Position.X + (width * 0.04f), shoulderY),
                new Vector2(x + (width * 0.05f), shoulderY - (height * 0.12f)),
                Ink, outlineWidth, antialiased: true);
            canvas.DrawLine(
                new Vector2(torso.End.X - (width * 0.04f), shoulderY),
                new Vector2(x + width - (width * 0.05f), shoulderY - (height * 0.12f)),
                Ink, outlineWidth, antialiased: true);
        }
        else
        {
            float normalHand1X = x + (width * 0.11f) - (stride * 0.25f);
            float normalHand1Y = hipY - (height * 0.02f);
            float normalHand2X = x + width - (width * 0.11f) + (stride * 0.25f);
            float normalHand2Y = hipY - (height * 0.02f);

            float hand1X = normalHand1X;
            float hand1Y = normalHand1Y;
            float hand2X = normalHand2X;
            float hand2Y = normalHand2Y;

            if (climbAmount > 0.001f)
            {
                float climbHand1Y = shoulderY - (height * 0.05f) - climbReach;
                float climbHand2Y = shoulderY + (height * 0.07f) + climbReach;

                hand1X = Mathf.Lerp(normalHand1X, climbWallLineX, climbAmount);
                hand1Y = Mathf.Lerp(normalHand1Y, climbHand1Y, climbAmount);
                hand2X = Mathf.Lerp(normalHand2X, climbWallLineX, climbAmount);
                hand2Y = Mathf.Lerp(normalHand2Y, climbHand2Y, climbAmount);

                float handSize = width * 0.16f * climbAmount;
                DrawBlob(canvas, new Vector2(hand1X, hand1Y), handSize / 2, outlineWidth);
                DrawBlob(canvas, new Vector2(hand2X, hand2Y), handSize / 2, outlineWidth);
            }

            canvas.DrawLine(
                new Vector2(torso.Position.X + (width * 0.04f), shoulderY),
                new Vector2(hand1X, hand1Y), Ink, outlineWidth, antialiased: true);
            canvas.DrawLine(
                new Vector2(torso.End.X - (width * 0.04f), shoulderY),
                new Vector2(hand2X, hand2Y), Ink, outlineWidth, antialiased: true);
        }

        DrawLeg(
            canvas, torso.Position.X + (width * 0.06f), hipY, x + (width * 0.27f) + stride,
            legBottom, width, crouch, mirrored: false, climbAmount, climbWallLineX, climbLegCycle, outlineWidth);
        DrawLeg(
            canvas, torso.End.X - (width * 0.06f), hipY, x + width - (width * 0.27f) - stride,
            legBottom, width, crouch, mirrored: true, climbAmount, climbWallLineX, -climbLegCycle, outlineWidth);
    }

    /// <summary>The whole character rigidly rotated about a pivot on the wall's own edge at
    /// shoulder height, so a straight vertical clip cuts the silhouette into a head that peeks
    /// out and a torso that stays hidden — rather than a barely-offset standing pose.</summary>
    private static void DrawHidePose(
        CanvasItem canvas,
        CharacterFrame frame,
        float x,
        float y,
        float width,
        float height,
        float outlineWidth,
        Color bodyColor)
    {
        float hideEase = Mathf.Clamp((float)frame.HideAmount, 0, 1);
        float pivotX = x + (width / 2);
        float pivotY = y + (height * 0.34f);
        float leanRadians = Mathf.DegToRad(frame.HidePeekDirection * 45f * hideEase);
        float sin = Mathf.Sin(leanRadians);
        float cos = Mathf.Cos(leanRadians);

        Vector2 FromPivot(float dx, float dy) => new(
            pivotX + (dx * cos) - (dy * sin),
            pivotY + (dx * sin) + (dy * cos));

        float peekHeadSize = width * 0.52f;

        // Head-to-pivot distance mirrors the normal pose's own head/torso ratio; an unrelated
        // fraction of overall height used to leave a visible gap that read as a floating head.
        float headOffset = peekHeadSize * 0.46f;
        Vector2 headCentre = FromPivot(0, -headOffset);

        // Torso sized to the normal pose's proportions exactly (height*0.34f). An earlier,
        // taller value fitted a window's clip more comfortably but made the body visibly
        // stretch as the rotation animated in.
        float torsoWidth = width * 0.42f;
        float torsoHeight = height * 0.34f;

        // Built in the pivot's own space and then rotated point by point, so the torso keeps the
        // same rounded corners as the standing pose. Drawing it as a plain four-point polygon
        // was visibly a sharp-cornered box the moment the character tucked away.
        Vector2[] torso = RoundedRectPolygon(
            new Rect2(-torsoWidth / 2, 0, torsoWidth, torsoHeight), torsoWidth * 0.3f);
        for (int point = 0; point < torso.Length; point++)
        {
            torso[point] = FromPivot(torso[point].X, torso[point].Y);
        }

        FillOutlined(canvas, torso, bodyColor, outlineWidth);

        // A hand braced on the wall edge itself: it stays on the wall plane (unrotated X)
        // rather than turning with the shoulder behind it.
        Vector2 armStart = FromPivot(0, height * 0.04f);
        var hand = new Vector2(pivotX, pivotY + (height * 0.12f));
        canvas.DrawLine(armStart, hand, Ink, outlineWidth, antialiased: true);
        DrawBlob(canvas, hand, width * 0.08f, outlineWidth);

        canvas.DrawCircle(headCentre, peekHeadSize / 2, Skin);
        canvas.DrawCircle(headCentre, peekHeadSize / 2, Ink, filled: false, width: outlineWidth, antialiased: true);

        float eyeSize = peekHeadSize * 0.19f;
        float eyeSpread = peekHeadSize * 0.145f;
        float eyeDrop = peekHeadSize * 0.02f;
        Vector2 eyeA = FromPivot(-eyeSpread, -headOffset + eyeDrop);
        Vector2 eyeB = FromPivot(eyeSpread, -headOffset + eyeDrop);
        DrawEye(canvas, eyeA.X - (eyeSize / 2), eyeA.Y - (eyeSize / 2), eyeSize, frame.GazeTarget);
        DrawEye(canvas, eyeB.X - (eyeSize / 2), eyeB.Y - (eyeSize / 2), eyeSize, frame.GazeTarget);
    }

    private static void DrawLeg(
        CanvasItem canvas,
        float hipX,
        float hipY,
        float footX,
        float footBottom,
        float width,
        float crouch,
        bool mirrored,
        float climbAmount,
        float wallLineX,
        float climbPhase,
        float outlineWidth)
    {
        float legSpan = footBottom - hipY;
        float standKneeX = hipX + ((footX - hipX) * 0.5f);
        float standKneeY = hipY + (legSpan * 0.5f);
        float standFootX = footX;
        bool bentStand = crouch >= 0.02f;
        if (bentStand)
        {
            float direction = mirrored ? 1 : -1;
            standKneeX = hipX + (direction * width * 0.36f * crouch);
            standKneeY = hipY + (legSpan * 0.34f * crouch);
            float crouchedFootX = hipX + (direction * width * 0.1f);
            standFootX = footX + ((crouchedFootX - footX) * crouch);
        }

        if (climbAmount <= 0.001f)
        {
            if (bentStand)
            {
                canvas.DrawLine(new Vector2(hipX, hipY), new Vector2(standKneeX, standKneeY), Ink, outlineWidth, antialiased: true);
                canvas.DrawLine(new Vector2(standKneeX, standKneeY), new Vector2(standFootX, footBottom), Ink, outlineWidth, antialiased: true);
            }
            else
            {
                canvas.DrawLine(new Vector2(hipX, hipY), new Vector2(footX, footBottom), Ink, outlineWidth, antialiased: true);
            }

            return;
        }

        // Both feet reach the same wall line the hands use; deriving each foot's reach from
        // its own hip made one leg overshoot the wall while the other never touched it.
        float climbKneeX = hipX + ((wallLineX - hipX) * 0.55f);
        float climbKneeY = hipY + (legSpan * 0.3f) + (climbPhase * legSpan * 0.08f);
        float climbFootY = footBottom - (legSpan * 0.1f) + (climbPhase * legSpan * 0.1f);

        var knee = new Vector2(
            Mathf.Lerp(standKneeX, climbKneeX, climbAmount),
            Mathf.Lerp(standKneeY, climbKneeY, climbAmount));
        var foot = new Vector2(
            Mathf.Lerp(standFootX, wallLineX, climbAmount),
            Mathf.Lerp(footBottom, climbFootY, climbAmount));

        canvas.DrawLine(new Vector2(hipX, hipY), knee, Ink, outlineWidth, antialiased: true);
        canvas.DrawLine(knee, foot, Ink, outlineWidth, antialiased: true);

        float footRadius = width * 0.065f * climbAmount;
        if (footRadius > 0.3f)
        {
            DrawBlob(canvas, foot, footRadius, outlineWidth);
        }
    }

    private static void DrawEye(CanvasItem canvas, float socketX, float socketY, float socketSize, Vec2 gazeTarget)
    {
        var centre = new Vector2(socketX + (socketSize / 2), socketY + (socketSize / 2));
        canvas.DrawCircle(centre, socketSize / 2, EyeWhite);

        double pupilRange = socketSize * 0.24;
        var toTarget = new Vec2(gazeTarget.X - centre.X, gazeTarget.Y - centre.Y);
        double distance = toTarget.Length;
        Vec2 offset = distance > 0.01
            ? toTarget * (Math.Min(pupilRange, distance) / distance)
            : Vec2.Zero;

        canvas.DrawCircle(
            centre + new Vector2((float)offset.X, (float)offset.Y),
            socketSize * 0.275f,
            Colors.Black);
    }

    private static void DrawBlob(CanvasItem canvas, Vector2 centre, float radius, float outlineWidth)
    {
        canvas.DrawCircle(centre, radius, Skin);
        canvas.DrawCircle(centre, radius, Ink, filled: false, width: outlineWidth, antialiased: true);
    }

    private static void DrawRoundedRect(CanvasItem canvas, Rect2 rect, float radius, Color fill, float outlineWidth) =>
        FillOutlined(canvas, RoundedRectPolygon(rect, radius), fill, outlineWidth);

    private static void FillOutlined(CanvasItem canvas, Vector2[] polygon, Color fill, float outlineWidth)
    {
        canvas.DrawColoredPolygon(polygon, fill);
        canvas.DrawPolyline([.. polygon, polygon[0]], Ink, outlineWidth, antialiased: true);
    }

    /// <summary>Godot has no rounded-rectangle primitive, so the torso is built as a polygon
    /// with arc-approximated corners. Returned rather than drawn, because the hide pose needs
    /// the same shape rotated about a pivot before it is painted.</summary>
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
