namespace DesktopPeople.Core.Platforms;

public sealed class PlatformRegistry
{
    private readonly object _gate = new();
    private PlatformSnapshot _snapshot = PlatformSnapshot.Empty;

    public PlatformSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Replace(PlatformSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _snapshot = snapshot;
        }
    }
}

public interface IWindowPlatformProvider : IDisposable
{
    PlatformSnapshot Snapshot { get; }

    void Start(RectD overlayScreenBounds, RectD virtualScreenBounds);

    void Pump(DateTimeOffset now, RectD overlayScreenBounds, RectD virtualScreenBounds);

    void SetExplicitlyExcludedHandles(IEnumerable<long> handles);
}
