using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;
using Godot;

namespace DesktopPeople.GodotHost;

/// <summary>
/// The developer view: every window the platform provider currently sees, the surfaces it
/// exposes, and a panel of the simulation's own internals. A port of the WinForms host's
/// <c>DrawPlatformDebug</c>/<c>DrawDebugPanel</c>.
/// <para>
/// It is a child node with its own canvas item rather than more drawing inside
/// <see cref="OverlayNode"/>'s <c>_Draw</c>, because that canvas item carries the hiding clip:
/// anything drawn there while the character hides behind a window is cut off at the same wall,
/// which would silently take the panel away exactly when something is worth looking at.
/// </para>
/// </summary>
internal sealed partial class DebugOverlay : Control
{
    private const int PanelWidth = 260;
    private const int PanelHeight = 300;
    private const int FontSize = 14;

    private static readonly Color BoundsColor = Color.Color8(68, 204, 255, 150);
    private static readonly Color SurfaceColor = Color.Color8(255, 196, 64, 230);
    private static readonly Color LabelColor = Color.Color8(255, 255, 255, 230);
    private static readonly Color LabelBackground = Color.Color8(24, 27, 37, 180);
    private static readonly Color PanelBackground = Color.Color8(27, 29, 40, 220);
    private static readonly Color FootColor = Colors.LimeGreen;
    private static readonly Color AttachmentColor = Colors.HotPink;

    private CharacterSimulation? _simulation;
    private IWindowPlatformProvider? _platforms;

    /// <summary>What the pointer last did to the character. Kept here because it is the one
    /// piece of debug state the simulation has no reason to remember.</summary>
    public string LastPointerEvent { get; set; } = "-";

    public void Attach(CharacterSimulation simulation, IWindowPlatformProvider platforms)
    {
        _simulation = simulation;
        _platforms = platforms;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        if (_simulation is null || _platforms is null)
        {
            return;
        }

        Font font = GetThemeDefaultFont();
        float ascent = font.GetAscent(FontSize);

        DrawPlatforms(font, ascent);
        DrawPanel(font, ascent);
    }

    private void DrawPlatforms(Font font, float ascent)
    {
        foreach (DesktopPlatform platform in _platforms!.Snapshot.Platforms)
        {
            RectD bounds = platform.Bounds;
            var rect = new Rect2(
                (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
            DrawRect(rect, BoundsColor, filled: false, width: 1);

            // The surfaces, not the window: a platform's walkable part is only the piece of its
            // top edge no other window covers, and telling those two apart by eye is most of
            // what this overlay is for.
            foreach (PlatformSegment segment in platform.Segments)
            {
                DrawLine(
                    new Vector2((float)segment.Left, (float)segment.SurfaceY),
                    new Vector2((float)segment.Right, (float)segment.SurfaceY),
                    SurfaceColor,
                    width: 3);
            }

            string label = $"{platform.Id}  HWND 0x{platform.ExternalHandle:X}";
            Vector2 labelSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize);
            DrawRect(
                new Rect2(rect.Position.X, rect.Position.Y - labelSize.Y, labelSize.X + 6, labelSize.Y),
                LabelBackground);
            DrawString(
                font,
                new Vector2(rect.Position.X + 3, rect.Position.Y - labelSize.Y + ascent),
                label,
                HorizontalAlignment.Left,
                -1,
                FontSize,
                LabelColor);
        }

        CharacterDiagnostics diagnostics = _simulation!.Diagnostics();
        DrawLine(
            new Vector2((float)diagnostics.FootInterval.Left, (float)diagnostics.Body.Bottom),
            new Vector2((float)diagnostics.FootInterval.Right, (float)diagnostics.Body.Bottom),
            FootColor,
            width: 4);

        if (diagnostics.AttachmentFootCenterX is { } attachmentX)
        {
            DrawCircle(
                new Vector2((float)attachmentX, (float)diagnostics.Body.Bottom), 5, AttachmentColor);
        }
    }

    private void DrawPanel(Font font, float ascent)
    {
        DrawRect(new Rect2(14, 14, PanelWidth, PanelHeight), PanelBackground);

        CharacterDiagnostics diagnostics = _simulation!.Diagnostics();
        string details =
            "DesktopPeople • DEBUG (Godot)\n" +
            $"FPS: {Engine.GetFramesPerSecond():F0}\n" +
            $"State: {diagnostics.State}\n" +
            $"Velocity: {diagnostics.Velocity.X:F0}, {diagnostics.Velocity.Y:F0}\n" +
            $"Platform: {diagnostics.CurrentPlatformId ?? "none"}\n" +
            $"Windows: {diagnostics.PlatformCount}\n" +
            $"Attached: {diagnostics.IsAttached}\n" +
            $"Mouse: {LastPointerEvent}\n" +
            $"Intensity: {diagnostics.BehaviorIntensity}  Energy: {diagnostics.CursorEnergy:F2}\n" +
            $"Harassment: {diagnostics.HarassmentLevel:F1}  Fleeing: {diagnostics.IsFleeing}\n" +
            $"Climbing: {diagnostics.IsClimbing}  Hiding: {diagnostics.HidingPlatformId ?? "-"}\n" +
            $"Scale: {diagnostics.CharacterScale:F2}";

        DrawMultilineString(
            font,
            new Vector2(26, 24 + ascent),
            details,
            HorizontalAlignment.Left,
            PanelWidth - 20,
            FontSize,
            -1,
            Colors.White);

        // The hit box, not the body: while hiding they are deliberately different, and a grab
        // that "misses" a visible character is exactly the kind of thing this shows at a glance.
        RectD interactive = _simulation.InteractiveBounds;
        DrawRect(
            new Rect2(
                (float)interactive.X,
                (float)interactive.Y,
                (float)interactive.Width,
                (float)interactive.Height),
            FootColor,
            filled: false,
            width: 1);
    }
}
