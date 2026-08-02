using System.Collections.Immutable;
using System.Diagnostics;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.Windows;

public sealed class WindowsWindowPlatformProvider : IWindowPlatformProvider
{
    private const double SurfaceOffset = 2;
    private const int MaximumEventsPerPump = 2_048;

    private readonly WindowSnapshotReader _reader;
    private readonly IWindowEventQueue _events;
    private readonly PlatformRegistry _registry;
    private readonly WindowFilter _filter;
    private readonly CoordinateMapper _coordinateMapper;
    private readonly IPlatformVisibilityPolicy _visibilityPolicy;
    private readonly TimeSpan _reconciliationInterval;
    private readonly Dictionary<long, WindowCandidate> _candidates = [];
    private readonly HashSet<long> _explicitlyExcludedHandles = [];
    private RectD _overlayScreenBounds;
    private RectD _virtualScreenBounds;
    private DateTimeOffset _lastReconciliation;
    private TimeSpan _totalUpdateDuration;
    private int _updateCount;
    private bool _started;
    private bool _disposed;

    public WindowsWindowPlatformProvider(
        IWindowApi api,
        IWindowEventQueue events,
        PlatformRegistry registry,
        int ownProcessId,
        TimeSpan? reconciliationInterval = null,
        IPlatformVisibilityPolicy? visibilityPolicy = null)
    {
        _reader = new WindowSnapshotReader(api);
        _events = events;
        _registry = registry;
        _filter = new WindowFilter(ownProcessId);
        _coordinateMapper = new CoordinateMapper();
        _visibilityPolicy = visibilityPolicy ?? new TopEdgeVisibilityPolicy();
        _reconciliationInterval = reconciliationInterval ?? TimeSpan.FromSeconds(5);
    }

    public event Action<PlatformProviderMetrics>? MetricsUpdated;

    public PlatformSnapshot Snapshot => _registry.Snapshot;

    public void Start(RectD overlayScreenBounds, RectD virtualScreenBounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlayScreenBounds = overlayScreenBounds;
        _virtualScreenBounds = virtualScreenBounds;
        Reconcile(DateTimeOffset.UtcNow);
        _started = true;
    }

    public void Pump(
        DateTimeOffset now,
        RectD overlayScreenBounds,
        RectD virtualScreenBounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            Start(overlayScreenBounds, virtualScreenBounds);
            return;
        }

        bool coordinateSpaceChanged =
            overlayScreenBounds != _overlayScreenBounds ||
            virtualScreenBounds != _virtualScreenBounds;
        _overlayScreenBounds = overlayScreenBounds;
        _virtualScreenBounds = virtualScreenBounds;

        bool changed = coordinateSpaceChanged;
        bool zOrderMayHaveChanged = false;
        int processedEvents = 0;
        while (processedEvents < MaximumEventsPerPump && _events.TryDequeue(out WindowChangeEvent windowEvent))
        {
            if (windowEvent.Kind == WindowChangeKind.ForegroundChanged)
            {
                zOrderMayHaveChanged = true;
            }
            else
            {
                changed |= ApplyEvent(windowEvent);
            }

            processedEvents++;
        }

        if (zOrderMayHaveChanged || now - _lastReconciliation >= _reconciliationInterval)
        {
            Reconcile(now);
            return;
        }

        if (changed)
        {
            PublishSnapshot(now, processedEvents, false);
        }
    }

    public void SetExplicitlyExcludedHandles(IEnumerable<long> handles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _explicitlyExcludedHandles.Clear();
        _explicitlyExcludedHandles.UnionWith(handles.Where(handle => handle != 0));
        if (_started)
        {
            PublishSnapshot(DateTimeOffset.UtcNow, 0, false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Dispose();
        _candidates.Clear();
        _registry.Replace(PlatformSnapshot.Empty);
    }

    private bool ApplyEvent(WindowChangeEvent windowEvent)
    {
        switch (windowEvent.Kind)
        {
            case WindowChangeKind.Destroy:
            case WindowChangeKind.Hide:
            case WindowChangeKind.MinimizeStart:
                return _candidates.Remove(windowEvent.Handle);

            case WindowChangeKind.Create:
            case WindowChangeKind.Show:
            case WindowChangeKind.MoveOrResize:
            case WindowChangeKind.MoveSizeEnd:
            case WindowChangeKind.MinimizeEnd:
                int zOrder = _candidates.TryGetValue(windowEvent.Handle, out WindowCandidate? existing)
                    ? existing.ZOrder
                    : 0;
                WindowCandidate updated = _reader.Read(new nint(windowEvent.Handle), zOrder);
                if (!updated.IsValid)
                {
                    return _candidates.Remove(windowEvent.Handle);
                }

                _candidates[windowEvent.Handle] = updated;
                return true;

            case WindowChangeKind.MoveSizeStart:
            default:
                return false;
        }
    }

    private void Reconcile(DateTimeOffset now)
    {
        var timer = Stopwatch.StartNew();
        IReadOnlyList<WindowCandidate> candidates = _reader.ReadAll();
        _candidates.Clear();
        foreach (WindowCandidate candidate in candidates)
        {
            if (candidate.IsValid)
            {
                _candidates[candidate.Handle] = candidate;
            }
        }

        _lastReconciliation = now;
        PublishSnapshot(now, 0, true, timer, candidates.Count);
    }

    private void PublishSnapshot(
        DateTimeOffset now,
        int processedEvents,
        bool reconciliation,
        Stopwatch? timer = null,
        int? enumeratedCount = null)
    {
        timer ??= Stopwatch.StartNew();
        var platforms = ImmutableArray.CreateBuilder<DesktopPlatform>();
        foreach (WindowCandidate candidate in _candidates.Values.OrderBy(value => value.ZOrder))
        {
            WindowFilterResult filterResult = _filter.Evaluate(
                candidate,
                _virtualScreenBounds,
                _explicitlyExcludedHandles);
            if (!filterResult.Accepted)
            {
                continue;
            }

            RectD bounds = _coordinateMapper.ScreenToOverlay(
                candidate.ScreenBounds,
                _overlayScreenBounds);
            double surfaceY = bounds.Y + SurfaceOffset;
            double ceilingY = bounds.Bottom - SurfaceOffset;
            platforms.Add(new DesktopPlatform
            {
                Id = $"window:{candidate.Handle:X}",
                Kind = PlatformKind.Window,
                ExternalHandle = candidate.Handle,
                Bounds = bounds,
                Segments = [new PlatformSegment(bounds.X, bounds.Right, surfaceY)],
                CeilingSegments = [new PlatformSegment(bounds.X, bounds.Right, ceilingY)],
                ZOrder = candidate.ZOrder,
                MonitorId = candidate.MonitorId,
                MonitorTop = candidate.MonitorTop - _overlayScreenBounds.Y,
                UpdatedAt = now,
            });
        }

        ImmutableArray<DesktopPlatform> visiblePlatforms = _visibilityPolicy.Apply(platforms.ToImmutable());
        timer.Stop();
        _totalUpdateDuration += timer.Elapsed;
        _updateCount++;
        var snapshot = new PlatformSnapshot
        {
            Platforms = visiblePlatforms,
            CapturedAt = now,
            EnumeratedWindowCount = enumeratedCount ?? _candidates.Count,
            UpdateDuration = timer.Elapsed,
        };
        _registry.Replace(snapshot);

        MetricsUpdated?.Invoke(new PlatformProviderMetrics(
            snapshot.EnumeratedWindowCount,
            snapshot.Platforms.Length,
            processedEvents,
            reconciliation,
            _reconciliationInterval,
            timer.Elapsed,
            TimeSpan.FromTicks(_totalUpdateDuration.Ticks / _updateCount)));
    }
}

public readonly record struct PlatformProviderMetrics(
    int EnumeratedWindowCount,
    int PlatformCount,
    int ProcessedEventCount,
    bool WasReconciliation,
    TimeSpan ReconciliationInterval,
    TimeSpan UpdateDuration,
    TimeSpan AverageUpdateDuration);
