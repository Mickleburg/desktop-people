namespace DesktopPeople.Core.Platforms;

public sealed class CoordinateMapper
{
    public RectD ScreenToOverlay(RectD screenBounds, RectD overlayScreenBounds) =>
        new(
            screenBounds.X - overlayScreenBounds.X,
            screenBounds.Y - overlayScreenBounds.Y,
            screenBounds.Width,
            screenBounds.Height);

    public Vec2 ScreenToOverlay(Vec2 screenPoint, RectD overlayScreenBounds) =>
        new(screenPoint.X - overlayScreenBounds.X, screenPoint.Y - overlayScreenBounds.Y);

    public RectD LogicalToPhysical(RectD logicalBounds, int dpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi));
        }

        double scale = dpi / 96d;
        return new RectD(
            logicalBounds.X * scale,
            logicalBounds.Y * scale,
            logicalBounds.Width * scale,
            logicalBounds.Height * scale);
    }
}
