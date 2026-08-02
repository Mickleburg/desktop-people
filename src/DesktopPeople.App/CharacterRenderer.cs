using System.Drawing.Drawing2D;
using DesktopPeople.Core;

namespace DesktopPeople.App;

internal readonly record struct CharacterPose(
    CharacterState State,
    double AnimationTime,
    bool Clicked,
    double CrouchAmount,
    Vec2 GazeTarget);

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
        bool locomoting = pose.State is CharacterState.Walk or CharacterState.Run;
        double cadence = running ? 15 : 11;
        float bobAmplitude = running ? 4.5f : 2.5f;
        float strideAmplitude = running ? 20f : 13f;
        float bob = locomoting ? (float)(Math.Sin(pose.AnimationTime * cadence) * bobAmplitude) : 0;
        float stride = locomoting ? (float)(Math.Sin(pose.AnimationTime * cadence) * strideAmplitude) : 0;
        // Sitting, the landing impact, and standing back up all share the same
        // knees-bent silhouette; only how CrouchAmount got to its value differs.
        float crouch = (float)(Math.Clamp(pose.CrouchAmount, 0, 1) * 24);

        using var outline = new Pen(Ink, 5)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var bodyBrush = new SolidBrush(pose.Clicked ? AccentWarm : Accent);
        using var faceBrush = new SolidBrush(Color.FromArgb(255, 222, 191));
        using var whiteBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.FromArgb(48, 30, 31, 43));

        graphics.FillEllipse(shadowBrush, x + 17, y + height - 8, width - 34, 10);

        float headSize = width * 0.52f;
        float headX = x + ((width - headSize) / 2);
        float headY = y + 3 + bob + crouch;
        graphics.FillEllipse(faceBrush, headX, headY, headSize, headSize);
        graphics.DrawEllipse(outline, headX, headY, headSize, headSize);

        float eyeY = headY + (headSize * 0.46f);
        DrawEye(graphics, headX + 16, eyeY, pose.GazeTarget, whiteBrush);
        DrawEye(graphics, headX + headSize - 25, eyeY, pose.GazeTarget, whiteBrush);

        float torsoTop = headY + headSize - 2;
        var torso = new RectangleF(x + 27, torsoTop, width - 54, height * 0.34f);
        graphics.FillRoundedRectangle(bodyBrush, torso, 14);
        graphics.DrawRoundedRectangle(outline, torso, 14);

        float shoulderY = torsoTop + 14;
        float hipY = torso.Bottom - 2;
        float legBottom = y + height - 9;

        if (pose.State == CharacterState.Fall)
        {
            graphics.DrawLine(outline, torso.Left + 4, shoulderY, x + 5, shoulderY - 22);
            graphics.DrawLine(outline, torso.Right - 4, shoulderY, x + width - 5, shoulderY - 22);
        }
        else
        {
            graphics.DrawLine(outline, torso.Left + 4, shoulderY, x + 10 - (stride * 0.25f), hipY - 4);
            graphics.DrawLine(outline, torso.Right - 4, shoulderY, x + width - 10 + (stride * 0.25f), hipY - 4);
        }

        graphics.DrawLine(outline, torso.Left + 14, hipY, x + 25 + stride, legBottom);
        graphics.DrawLine(outline, torso.Right - 14, hipY, x + width - 25 - stride, legBottom);

        if (pose.State == CharacterState.HeldByMouse)
        {
            using var heldBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            graphics.FillEllipse(heldBrush, x + width - 24, y - 4, 24, 24);
            graphics.DrawEllipse(outline, x + width - 24, y - 4, 24, 24);
        }
    }

    private static void DrawEye(Graphics graphics, float socketX, float socketY, Vec2 gazeTarget, Brush whiteBrush)
    {
        graphics.FillEllipse(whiteBrush, socketX, socketY, 9, 9);

        const double pupilRange = 2.2;
        var eyeCenter = new Vec2(socketX + 4.5, socketY + 4.5);
        Vec2 toTarget = gazeTarget - eyeCenter;
        double distance = toTarget.Length;
        Vec2 pupilOffset = distance > 0.01 ? toTarget * (Math.Min(pupilRange, distance) / distance) : Vec2.Zero;

        graphics.FillEllipse(
            Brushes.Black,
            (float)(socketX + 3 + pupilOffset.X),
            (float)(socketY + 2 + pupilOffset.Y),
            5,
            5);
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

