namespace DesktopPeople.Core.Platforms;

public sealed record WindowCandidate
{
    public required long Handle { get; init; }
    public bool IsValid { get; init; }
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
    public required RectD ScreenBounds { get; init; }
    public bool UsedDwmBounds { get; init; }
    public long Style { get; init; }
    public long ExtendedStyle { get; init; }
    public int ProcessId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int ZOrder { get; init; }
    public string MonitorId { get; init; } = string.Empty;
    public double MonitorTop { get; init; } = double.NegativeInfinity;
}

public enum WindowExclusionReason
{
    None,
    InvalidHandle,
    Invisible,
    Minimized,
    OwnProcess,
    ExplicitlyExcluded,
    InvalidBounds,
    TooSmall,
    OutsideVirtualScreen,
    ChildWindow,
    ToolWindow,
    NoActivateWindow,
    ServiceClass,
}

public readonly record struct WindowFilterResult(bool Accepted, WindowExclusionReason Reason)
{
    public static WindowFilterResult Include => new(true, WindowExclusionReason.None);
    public static WindowFilterResult Exclude(WindowExclusionReason reason) => new(false, reason);
}
