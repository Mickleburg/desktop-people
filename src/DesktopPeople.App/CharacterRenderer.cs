using System.Drawing.Drawing2D;
using DesktopPeople.Core;

namespace DesktopPeople.App;

internal readonly record struct CharacterPose(
    CharacterState State,
    double AnimationTime,
    bool Clicked,
    double CrouchAmount,
    Vec2 GazeTarget,
    int ClimbWallDirection = 1,
    bool ShowShadow = false,
    double ClimbAmount = 0,
    int HidePeekDirection = 1);

internal sealed class CharacterRenderer
{
    private static readonly Color Ink = Color.FromArgb(41, 45, 62);
    private static readonly Color Accent = Color.FromArgb(111, 92, 255);
    private static readonly Color AccentWarm = Color.FromArgb(255, 125, 94);

    public void Draw(Graphics graphics, RectD body, CharacterPose pose)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float x = (float)body.X;
        float y = (float)body.Y;
        float width = (float)body.Width;
        float height = (float)body.Height;

        bool running = pose.State == CharacterState.Run;
        bool climbing = pose.State == CharacterState.Climb;
        bool locomoting = pose.State is CharacterState.Walk or CharacterState.Run;
        float climbAmount = Math.Clamp((float)pose.ClimbAmount, 0, 1);
        double cadence = running ? 15 : (climbing ? 7 : 11);
        float bobAmplitude = running ? width * 0.096f : width * 0.053f;
        float strideAmplitude = running ? width * 0.43f : width * 0.28f;
        float bob = locomoting ? (float)(Math.Sin(pose.AnimationTime * cadence) * bobAmplitude) : 0;
        float stride = locomoting ? (float)(Math.Sin(pose.AnimationTime * cadence) * strideAmplitude) : 0;
        // Gated on `climbing` (the actual state), not just `climbAmount` > 0: the amount
        // still eases the reach in smoothly as a climb starts, but the oscillation itself
        // must stop dead the instant climbing ends, or the arms/legs keep pawing at the air
        // for the whole blend-out — reading as still grabbing at a wall that isn't there.
        float climbReach = climbing
            ? (float)(Math.Sin(pose.AnimationTime * cadence) * height * 0.07 * climbAmount)
            : 0;
        float climbLegCycle = climbing ? (float)Math.Sin(pose.AnimationTime * cadence) : 0;

        // Sitting, the landing impact, and standing back up all share the same
        // knees-bent silhouette; only how CrouchAmount got there (locked vs. decaying) differs.
        float crouch = Math.Clamp((float)pose.CrouchAmount, 0, 1);

        float outlineWidth = Math.Max(1.6f, width * 0.055f);
        using var outline = new Pen(Ink, outlineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var bodyBrush = new SolidBrush(pose.Clicked ? AccentWarm : Accent);
        using var faceBrush = new SolidBrush(Color.FromArgb(255, 222, 191));
        using var whiteBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.FromArgb(48, 30, 31, 43));

        float legBottom = y + height - (height * 0.05f);
        if (pose.ShowShadow)
        {
            float shadowWidth = width * (0.66f - (crouch * 0.1f));
            graphics.FillEllipse(
                shadowBrush,
                x + ((width - shadowWidth) / 2),
                legBottom - (height * 0.02f),
                shadowWidth,
                height * 0.05f);
        }

        if (pose.State == CharacterState.Hide)
        {
            // Draws a mostly-normal angled body — head, a real torso (not just a collar
            // sliver), one braced arm — rather than a bespoke small peek shape. OverlayForm
            // clips painting against the wall's own rectangle before calling Draw, so
            // whatever part of this naturally overlaps the wall (the far side, away from
            // HidePeekDirection) is genuinely cut off there, the way real occlusion would
            // look, while the near side leaning out stays fully visible.
            float peekHeadSize = width * 0.52f;
            float lean = pose.HidePeekDirection * width * 0.16f;
            float peekHeadX = x + ((width - peekHeadSize) / 2) + lean;
            float peekHeadY = y + (height * 0.017f);

            float peekTorsoWidth = width * 0.42f;
            float peekTorsoHeight = height * 0.62f;
            var peekTorso = new RectangleF(
                x + ((width - peekTorsoWidth) / 2) + (lean * 0.3f),
                peekHeadY + (peekHeadSize * 0.8f),
                peekTorsoWidth,
                peekTorsoHeight);
            graphics.FillRoundedRectangle(bodyBrush, peekTorso, peekTorso.Width * 0.25f);
            graphics.DrawRoundedRectangle(outline, peekTorso, peekTorso.Width * 0.25f);

            // A hand braced right at the edge being hidden behind — anchored near the
            // torso's wall-side (opposite the lean) rather than out where the head is
            // leaning — reads as holding on to peek out, instead of a head floating free of
            // whatever it's supposedly hiding against.
            float peekHandAnchorX = pose.HidePeekDirection > 0 ? peekTorso.Left : peekTorso.Right;
            float peekHandAnchorY = peekTorso.Top + (peekTorso.Height * 0.12f);
            float peekHandX = x + (width * 0.5f) - (pose.HidePeekDirection * width * 0.05f);
            float peekHandY = peekHandAnchorY + (height * 0.18f);
            float peekHandSize = width * 0.16f;
            graphics.DrawLine(outline, peekHandAnchorX, peekHandAnchorY, peekHandX, peekHandY);
            graphics.FillEllipse(
                faceBrush, peekHandX - (peekHandSize / 2), peekHandY - (peekHandSize / 2), peekHandSize, peekHandSize);
            graphics.DrawEllipse(
                outline, peekHandX - (peekHandSize / 2), peekHandY - (peekHandSize / 2), peekHandSize, peekHandSize);

            graphics.FillEllipse(faceBrush, peekHeadX, peekHeadY, peekHeadSize, peekHeadSize);
            graphics.DrawEllipse(outline, peekHeadX, peekHeadY, peekHeadSize, peekHeadSize);

            float peekEyeSize = peekHeadSize * 0.19f;
            float peekEyeY = peekHeadY + (peekHeadSize * 0.44f);
            float eyeLean = pose.HidePeekDirection * peekHeadSize * 0.06f;
            DrawEye(
                graphics, peekHeadX + (peekHeadSize * 0.26f) + eyeLean, peekEyeY, peekEyeSize, pose.GazeTarget, whiteBrush);
            DrawEye(
                graphics,
                peekHeadX + (peekHeadSize * 0.74f) - peekEyeSize + eyeLean,
                peekEyeY,
                peekEyeSize,
                pose.GazeTarget,
                whiteBrush);
            return;
        }

        // Crouching lowers the head/torso within the same footprint (feet stay planted)
        // and widens the silhouette slightly, reading as a settle/impact rather than a
        // uniform vertical shift.
        float crouchDrop = crouch * height * 0.26f;
        float stretch = crouch * width * 0.1f;

        // Leans the whole upper body toward the wall it's clinging to (not just the arms
        // reaching out from an otherwise front-facing stand) so climbing reads as actually
        // pressed against the surface; eases with ClimbAmount like everything else here, so
        // it slides in/out smoothly rather than popping.
        float climbLean = climbAmount * pose.ClimbWallDirection * width * 0.16f;

        float headSize = width * 0.52f;
        float headX = x + ((width - headSize) / 2) + climbLean;
        float headY = y + (height * 0.017f) + bob + crouchDrop;
        graphics.FillEllipse(faceBrush, headX, headY, headSize, headSize);
        graphics.DrawEllipse(outline, headX, headY, headSize, headSize);

        float eyeSize = headSize * 0.19f;
        float eyeY = headY + (headSize * 0.44f);
        DrawEye(graphics, headX + (headSize * 0.26f), eyeY, eyeSize, pose.GazeTarget, whiteBrush);
        DrawEye(graphics, headX + (headSize * 0.74f) - eyeSize, eyeY, eyeSize, pose.GazeTarget, whiteBrush);

        float torsoTop = headY + (headSize * 0.96f);
        float torsoHeight = height * 0.34f;
        var torso = new RectangleF(
            x + (width * 0.29f) - (stretch / 2) + climbLean,
            torsoTop,
            (width * 0.42f) + stretch,
            torsoHeight);
        graphics.FillRoundedRectangle(bodyBrush, torso, torso.Width * 0.3f);
        graphics.DrawRoundedRectangle(outline, torso, torso.Width * 0.3f);

        float shoulderY = torsoTop + (height * 0.08f);
        float hipY = torso.Bottom - (height * 0.01f);

        if (pose.State == CharacterState.Fall)
        {
            graphics.DrawLine(outline, torso.Left + (width * 0.04f), shoulderY, x + (width * 0.05f), shoulderY - (height * 0.12f));
            graphics.DrawLine(outline, torso.Right - (width * 0.04f), shoulderY, x + width - (width * 0.05f), shoulderY - (height * 0.12f));
        }
        else
        {
            // Blended by ClimbAmount rather than switched on CharacterState.Climb: entering
            // or leaving a climb cross-fades the arms from the normal swing to gripping the
            // wall over ClimbPoseBlendSeconds instead of swapping poses on a single frame.
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
                // Both hands reach sideways to the wall they're actually clinging to (not
                // symmetrically up into open air) and land on a visible grip point at its
                // edge, so it reads as holding on rather than dangling.
                float wallX = pose.ClimbWallDirection > 0 ? x + width + (width * 0.03f) : x - (width * 0.03f);
                float climbHand1Y = shoulderY - (height * 0.05f) - climbReach;
                float climbHand2Y = shoulderY + (height * 0.07f) + climbReach;

                hand1X = Lerp(normalHand1X, wallX, climbAmount);
                hand1Y = Lerp(normalHand1Y, climbHand1Y, climbAmount);
                hand2X = Lerp(normalHand2X, wallX, climbAmount);
                hand2Y = Lerp(normalHand2Y, climbHand2Y, climbAmount);

                float handSize = width * 0.16f * climbAmount;
                graphics.FillEllipse(faceBrush, hand1X - (handSize / 2), hand1Y - (handSize / 2), handSize, handSize);
                graphics.DrawEllipse(outline, hand1X - (handSize / 2), hand1Y - (handSize / 2), handSize, handSize);
                graphics.FillEllipse(faceBrush, hand2X - (handSize / 2), hand2Y - (handSize / 2), handSize, handSize);
                graphics.DrawEllipse(outline, hand2X - (handSize / 2), hand2Y - (handSize / 2), handSize, handSize);
            }

            graphics.DrawLine(outline, torso.Left + (width * 0.04f), shoulderY, hand1X, hand1Y);
            graphics.DrawLine(outline, torso.Right - (width * 0.04f), shoulderY, hand2X, hand2Y);
        }

        DrawLeg(
            graphics, outline, faceBrush, torso.Left + (width * 0.06f), hipY, x + (width * 0.27f) + stride,
            legBottom, width, crouch, mirrored: false, climbAmount, pose.ClimbWallDirection, climbLegCycle);
        DrawLeg(
            graphics, outline, faceBrush, torso.Right - (width * 0.06f), hipY, x + width - (width * 0.27f) - stride,
            legBottom, width, crouch, mirrored: true, climbAmount, pose.ClimbWallDirection, -climbLegCycle);
    }

    private static void DrawLeg(
        Graphics graphics,
        Pen outline,
        Brush faceBrush,
        float hipX,
        float hipY,
        float footX,
        float footBottom,
        float width,
        float crouch,
        bool mirrored,
        float climbAmount,
        int wallDirection,
        float climbPhase)
    {
        // A knee pushed out to the side and down — clearly below and away from the
        // hip — reads as "bent leg" far more clearly than a shortened straight line.
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
                graphics.DrawLine(outline, hipX, hipY, standKneeX, standKneeY);
                graphics.DrawLine(outline, standKneeX, standKneeY, standFootX, footBottom);
            }
            else
            {
                graphics.DrawLine(outline, hipX, hipY, footX, footBottom);
            }

            return;
        }

        // Climbing presses the whole body to the wall (see climbLean in Draw()), and the
        // legs follow suit: knee bent toward the wall face, foot planted against it, with a
        // per-leg phase offset (opposite the other leg, like a normal gait) so they
        // alternate instead of both hanging static while only the arms do any work.
        float climbKneeX = hipX + (wallDirection * width * 0.24f);
        float climbKneeY = hipY + (legSpan * 0.3f) + (climbPhase * legSpan * 0.08f);
        float climbFootX = hipX + (wallDirection * width * 0.32f);
        float climbFootY = footBottom - (legSpan * 0.1f) + (climbPhase * legSpan * 0.1f);

        float kneeX = Lerp(standKneeX, climbKneeX, climbAmount);
        float kneeY = Lerp(standKneeY, climbKneeY, climbAmount);
        float footEndX = Lerp(standFootX, climbFootX, climbAmount);
        float footEndY = Lerp(footBottom, climbFootY, climbAmount);

        graphics.DrawLine(outline, hipX, hipY, kneeX, kneeY);
        graphics.DrawLine(outline, kneeX, kneeY, footEndX, footEndY);

        float footSize = width * 0.13f * climbAmount;
        if (footSize > 0.6f)
        {
            graphics.FillEllipse(faceBrush, footEndX - (footSize / 2), footEndY - (footSize / 2), footSize, footSize);
            graphics.DrawEllipse(outline, footEndX - (footSize / 2), footEndY - (footSize / 2), footSize, footSize);
        }
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static void DrawEye(
        Graphics graphics,
        float socketX,
        float socketY,
        float socketSize,
        Vec2 gazeTarget,
        Brush whiteBrush)
    {
        graphics.FillEllipse(whiteBrush, socketX, socketY, socketSize, socketSize);

        double pupilRange = socketSize * 0.24;
        var eyeCenter = new Vec2(socketX + (socketSize / 2), socketY + (socketSize / 2));
        Vec2 toTarget = gazeTarget - eyeCenter;
        double distance = toTarget.Length;
        Vec2 pupilOffset = distance > 0.01 ? toTarget * (Math.Min(pupilRange, distance) / distance) : Vec2.Zero;

        float pupilSize = socketSize * 0.55f;
        graphics.FillEllipse(
            Brushes.Black,
            (float)(socketX + ((socketSize - pupilSize) / 2) + pupilOffset.X),
            (float)(socketY + ((socketSize - pupilSize) / 2) + pupilOffset.Y),
            pupilSize,
            pupilSize);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        RectangleF bounds,
        float radius)
    {
        using GraphicsPath path = CreateRoundedPath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(
        this Graphics graphics,
        Pen pen,
        RectangleF bounds,
        float radius)
    {
        using GraphicsPath path = CreateRoundedPath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
