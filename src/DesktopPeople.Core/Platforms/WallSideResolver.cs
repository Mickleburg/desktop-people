namespace DesktopPeople.Core.Platforms;

/// <summary>
/// Centralizes the "which edge does this motion cling to" arithmetic so it can be unit
/// tested — it previously lived as an inline ternary in the WinForms layer and had the
/// two cases swapped (walking off a platform's own right edge clung to its left edge
/// instead), which was invisible until manually walking a character off an edge.
/// </summary>
public static class WallSideResolver
{
    /// <summary>The edge of the platform a character is standing on that it clings to when
    /// walking off it in <paramref name="walkDirection"/>: moving right walks off the
    /// platform's own right edge, moving left walks off its own left edge.</summary>
    public static WallSide ForOwnEdge(int walkDirection) =>
        walkDirection >= 0 ? WallSide.Right : WallSide.Left;

    /// <summary>The edge of a different, obstructing platform a character clings to when
    /// approaching it from <paramref name="approachDirection"/>: approaching from the left
    /// (moving right) touches that platform's left edge, and vice versa.</summary>
    public static WallSide ForEncounteredWall(double approachDirection) =>
        approachDirection >= 0 ? WallSide.Left : WallSide.Right;
}
