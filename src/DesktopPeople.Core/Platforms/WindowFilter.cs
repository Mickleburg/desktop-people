namespace DesktopPeople.Core.Platforms;

public sealed class WindowFilter
{
    public const long WsChild = 0x40000000L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExNoActivate = 0x08000000L;

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "#32768",
        "Dwm",
        "Progman",
        "Shell_SecondaryTrayWnd",
        "Shell_TrayWnd",
        "SysShadow",
        "tooltips_class32",
        "WorkerW",
    };

    private readonly int _ownProcessId;
    private readonly double _minimumWidth;
    private readonly double _minimumHeight;

    public WindowFilter(int ownProcessId, double minimumWidth = 120, double minimumHeight = 36)
    {
        _ownProcessId = ownProcessId;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
    }

    public WindowFilterResult Evaluate(
        WindowCandidate candidate,
        RectD virtualScreen,
        IReadOnlySet<long>? explicitlyExcludedHandles = null)
    {
        if (!candidate.IsValid)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.InvalidHandle);
        }

        if (!candidate.IsVisible)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.Invisible);
        }

        if (candidate.IsMinimized)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.Minimized);
        }

        if (candidate.ProcessId == _ownProcessId)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.OwnProcess);
        }

        if (explicitlyExcludedHandles?.Contains(candidate.Handle) == true)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.ExplicitlyExcluded);
        }

        RectD bounds = candidate.ScreenBounds;
        if (!double.IsFinite(bounds.X) ||
            !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) ||
            !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.InvalidBounds);
        }

        if (bounds.Width < _minimumWidth || bounds.Height < _minimumHeight)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.TooSmall);
        }

        if (!bounds.Intersects(virtualScreen))
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.OutsideVirtualScreen);
        }

        if ((candidate.Style & WsChild) != 0)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.ChildWindow);
        }

        if ((candidate.ExtendedStyle & WsExToolWindow) != 0)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.ToolWindow);
        }

        if ((candidate.ExtendedStyle & WsExNoActivate) != 0)
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.NoActivateWindow);
        }

        if (ExcludedClasses.Contains(candidate.ClassName))
        {
            return WindowFilterResult.Exclude(WindowExclusionReason.ServiceClass);
        }

        return WindowFilterResult.Include;
    }
}
