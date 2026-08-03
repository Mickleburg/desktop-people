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
    int HidePeekDirection = 1,
    double HideAmount = 1);

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

        // The actual wall face being climbed, in the same coordinate space as everything
        // else here — shared by both hands and both legs so all four limbs reach for the
        // same line instead of each computing its own approximation and drifting apart
        // (previously the legs derived their reach from their own hip X, so one leg
        // consistently overshot past the wall while the other fell short and never
        // actually touched it, reading as pawing at open air).
        float climbWallLineX = pose.ClimbWallDirection > 0 ? x + width + (width * 0.03f) : x - (width * 0.03f);

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
            // Picture the whole character rigidly rotated ~45° around a pivot that sits
            // exactly on the wall's own edge at shoulder height — the same X OverlayForm
            // clips paint against. A single rotation swings whatever is above the pivot
            // (the head) out to the peek side while swinging whatever hangs below it (the
            // torso) to the wall side, so the vertical clip genuinely cuts the silhouette
            // into a "leaning around the corner" shape instead of just trimming a
            // barely-offset standing pose. HideAmount eases the rotation in from 0 (a
            // normal upright bust) up to full lean as the character settles in, in lockstep
            // with OverlayForm's position slide, instead of popping straight into the
            // rotated stance on the first Hide frame.
            float hideEase = Math.Clamp((float)pose.HideAmount, 0, 1);
            float pivotX = x + (width / 2);
            float pivotY = y + (height * 0.34f);
            float leanAngleDegrees = pose.HidePeekDirection * 45f * hideEase;
            double leanAngleRadians = leanAngleDegrees * Math.PI / 180.0;
            float sin = (float)Math.Sin(leanAngleRadians);
            float cos = (float)Math.Cos(leanAngleRadians);

            (float X, float Y) RotateFromPivot(float localDx, float localDy) => (
                pivotX + (localDx * cos) - (localDy * sin),
                pivotY + (localDx * sin) + (localDy * cos));

            float peekHeadSize = width * 0.52f;

            // Head-to-pivot distance mirrors the same ratio the normal (non-Hide) pose uses
            // between its head and torso — torsoTop sits at headSize*0.96 below the head's
            // own top edge there, i.e. headSize*0.46 below its center — instead of an
            // unrelated fraction of overall height. The previous value put the head's own
            // edge a visible gap away from the pivot (where the torso's top is anchored),
            // reading as a head floating disconnected above the body.
            float headOffset = peekHeadSize * 0.46f;
            (float headCenterX, float headCenterY) = RotateFromPivot(0, -headOffset);

            // The torso is built as a normal, unrotated rounded rect hanging straight down
            // from the pivot (top-center exactly at the pivot point), then the whole path
            // is rotated around that same pivot — a real geometric rotation of the shape
            // itself, not just its anchor point, so the torso's edge reads as genuinely
            // turned rather than merely translated. Sized to match the normal (non-Hide)
            // pose's torso proportions exactly (torsoHeight below is height*0.34f too) —
            // an earlier, taller value (0.56f) was picked to comfortably fit inside a real
            // window's clip, but it made the torso visibly longer than the character's
            // actual body: most noticeable right as a hide starts (before much rotation has
            // clipped it away, the oversized rect reads as the torso stretching), and it
            // remained a slightly wrong proportion in the fully-hidden sliver too. Windows
            // are comfortably taller than the character regardless, so there's no need to
            // over-extend past the real proportions to stay hidden.
            float peekTorsoWidth = width * 0.42f;
            float peekTorsoHeight = height * 0.34f;
            var localTorso = new RectangleF(pivotX - (peekTorsoWidth / 2), pivotY, peekTorsoWidth, peekTorsoHeight);
            using (GraphicsPath torsoPath = GraphicsExtensions.CreateRoundedPath(localTorso, peekTorsoWidth * 0.25f))
            using (var rotation = new Matrix())
            {
                rotation.RotateAt(leanAngleDegrees, new PointF(pivotX, pivotY));
                torsoPath.Transform(rotation);
                graphics.FillPath(bodyBrush, torsoPath);
                graphics.DrawPath(outline, torsoPath);
            }

            // A hand braced against the wall edge itself, noticeably lower than the
            // head/shoulder junction (chest height, not neck height) so it reads as a
            // distinct hand on the corner rather than a stray head-colored fragment sitting
            // right where the head and torso already meet. It stays on the actual wall edge
            // line (X = pivotX, unrotated) rather than rotating with the body — a hand
            // pressed flat against a flat wall stays on that wall's plane regardless of how
            // the shoulder behind it twists.
            (float armStartX, float armStartY) = RotateFromPivot(0, height * 0.04f);
            float handX = pivotX;
            float handY = pivotY + (height * 0.12f);
            float peekHandSize = width * 0.16f;
            graphics.DrawLine(outline, armStartX, armStartY, handX, handY);
            graphics.FillEllipse(faceBrush, handX - (peekHandSize / 2), handY - (peekHandSize / 2), peekHandSize, peekHandSize);
            graphics.DrawEllipse(outline, handX - (peekHandSize / 2), handY - (peekHandSize / 2), peekHandSize, peekHandSize);

            graphics.FillEllipse(faceBrush, headCenterX - (peekHeadSize / 2), headCenterY - (peekHeadSize / 2), peekHeadSize, peekHeadSize);
            graphics.DrawEllipse(outline, headCenterX - (peekHeadSize / 2), headCenterY - (peekHeadSize / 2), peekHeadSize, peekHeadSize);

            // Eyes are offset from the head's own (unrotated) center and carried through the
            // same rotation as the head, so the face itself reads as tilted with the lean
            // instead of staying artificially level on a turned head.
            float peekEyeSize = peekHeadSize * 0.19f;
            float eyeSpread = peekHeadSize * 0.145f;
            float eyeDrop = peekHeadSize * 0.02f;
            (float eyeAX, float eyeAY) = RotateFromPivot(-eyeSpread, -headOffset + eyeDrop);
            (float eyeBX, float eyeBY) = RotateFromPivot(eyeSpread, -headOffset + eyeDrop);
            DrawEye(graphics, eyeAX - (peekEyeSize / 2), eyeAY - (peekEyeSize / 2), peekEyeSize, pose.GazeTarget, whiteBrush);
            DrawEye(graphics, eyeBX - (peekEyeSize / 2), eyeBY - (peekEyeSize / 2), peekEyeSize, pose.GazeTarget, whiteBrush);
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
                float climbHand1Y = shoulderY - (height * 0.05f) - climbReach;
                float climbHand2Y = shoulderY + (height * 0.07f) + climbReach;

                hand1X = Lerp(normalHand1X, climbWallLineX, climbAmount);
                hand1Y = Lerp(normalHand1Y, climbHand1Y, climbAmount);
                hand2X = Lerp(normalHand2X, climbWallLineX, climbAmount);
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
            legBottom, width, crouch, mirrored: false, climbAmount, climbWallLineX, climbLegCycle);
        DrawLeg(
            graphics, outline, faceBrush, torso.Right - (width * 0.06f), hipY, x + width - (width * 0.27f) - stride,
            legBottom, width, crouch, mirrored: true, climbAmount, climbWallLineX, -climbLegCycle);
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
        float wallLineX,
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
        // alternate instead of both hanging static while only the arms do any work. Both
        // legs' feet reach the same wallLineX the hands use (previously each leg derived
        // its own reach from its own hip X, so one consistently overshot past the wall
        // while the other fell short and never actually touched it — pawing at open air).
        float climbKneeX = hipX + ((wallLineX - hipX) * 0.55f);
        float climbKneeY = hipY + (legSpan * 0.3f) + (climbPhase * legSpan * 0.08f);
        float climbFootX = wallLineX;
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

    internal static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
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
