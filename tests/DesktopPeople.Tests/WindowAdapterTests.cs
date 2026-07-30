using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;
using DesktopPeople.Windows;

namespace DesktopPeople.Tests;

internal static class WindowAdapterTests
{
    private static readonly RectD OverlayBounds = new(-1_920, 0, 4_480, 1_440);
    private static readonly RectD VirtualBounds = new(-1_920, 0, 4_480, 1_440);

    public static TestCase[] All =>
    [
        new("invalid HWND is safely ignored", InvalidHandleIgnored),
        new("DWM bounds are preferred when available", DwmBoundsPreferred),
        new("GetWindowRect is used as a fallback", WindowRectFallback),
        new("normal Win32 window reaches the platform registry", ProviderCreatesPlatform),
        new("own overlay never reaches the platform registry", ProviderExcludesOverlay),
        new("window move event updates platform without full scan", MoveEventUpdatesPlatform),
        new("window minimize event removes platform", MinimizeRemovesPlatform),
        new("window restore event recreates platform", RestoreRecreatesPlatform),
        new("window hide event removes platform", HideRemovesPlatform),
        new("event sequence create move minimize destroy is stable", EventSequenceIsStable),
        new("periodic reconciliation recovers missed events", ReconciliationRecoversMissedEvent),
    ];

    private static void InvalidHandleIgnored()
    {
        var api = new FakeWindowApi();
        api.Add(new FakeWindowData { Handle = 10, IsValid = false });
        WindowCandidate candidate = new WindowSnapshotReader(api).Read(new nint(10), 0);
        AssertEx.False(candidate.IsValid);
    }

    private static void DwmBoundsPreferred()
    {
        var api = new FakeWindowApi();
        api.Add(new FakeWindowData
        {
            Handle = 10,
            DwmBounds = new RectD(101, 102, 700, 500),
            WindowBounds = new RectD(90, 90, 730, 530),
        });
        WindowCandidate candidate = new WindowSnapshotReader(api).Read(new nint(10), 0);
        AssertEx.True(candidate.UsedDwmBounds);
        AssertEx.Equal(new RectD(101, 102, 700, 500), candidate.ScreenBounds);
        AssertEx.Equal(0, api.WindowRectCallCount);
    }

    private static void WindowRectFallback()
    {
        var api = new FakeWindowApi();
        api.Add(new FakeWindowData
        {
            Handle = 10,
            WindowBounds = new RectD(90, 90, 730, 530),
        });
        WindowCandidate candidate = new WindowSnapshotReader(api).Read(new nint(10), 0);
        AssertEx.False(candidate.UsedDwmBounds);
        AssertEx.Equal(new RectD(90, 90, 730, 530), candidate.ScreenBounds);
        AssertEx.Equal(1, api.WindowRectCallCount);
    }

    private static void ProviderCreatesPlatform()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out _);
        provider.Start(OverlayBounds, VirtualBounds);
        DesktopPlatform platform = provider.Snapshot.Platforms.Single();
        AssertEx.Equal("window:A", platform.Id);
        AssertEx.Equal(new RectD(2_020, 200, 800, 600), platform.Bounds);
        AssertEx.Near(202, platform.Segments[0].SurfaceY);
    }

    private static void ProviderExcludesOverlay()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out _);
        provider.SetExplicitlyExcludedHandles([10]);
        provider.Start(OverlayBounds, VirtualBounds);
        AssertEx.Equal(0, provider.Snapshot.Platforms.Length);
    }

    private static void MoveEventUpdatesPlatform()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out InMemoryWindowEventQueue queue);
        provider.Start(OverlayBounds, VirtualBounds);
        api.Get(10).DwmBounds = new RectD(300, 250, 800, 600);
        queue.Enqueue(Event(10, WindowChangeKind.MoveOrResize));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Near(2_220, provider.Snapshot.Platforms.Single().Bounds.X);
        AssertEx.Equal(1, api.EnumerationCount);
    }

    private static void MinimizeRemovesPlatform()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out InMemoryWindowEventQueue queue);
        provider.Start(OverlayBounds, VirtualBounds);
        api.Get(10).IsMinimized = true;
        queue.Enqueue(Event(10, WindowChangeKind.MinimizeStart));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Equal(0, provider.Snapshot.Platforms.Length);
    }

    private static void RestoreRecreatesPlatform()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out InMemoryWindowEventQueue queue);
        provider.Start(OverlayBounds, VirtualBounds);
        queue.Enqueue(Event(10, WindowChangeKind.MinimizeStart));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        api.Get(10).IsMinimized = false;
        queue.Enqueue(Event(10, WindowChangeKind.MinimizeEnd));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Equal(1, provider.Snapshot.Platforms.Length);
    }

    private static void HideRemovesPlatform()
    {
        var api = new FakeWindowApi();
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        using WindowsWindowPlatformProvider provider = Provider(api, out InMemoryWindowEventQueue queue);
        provider.Start(OverlayBounds, VirtualBounds);
        queue.Enqueue(Event(10, WindowChangeKind.Hide));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Equal(0, provider.Snapshot.Platforms.Length);
    }

    private static void EventSequenceIsStable()
    {
        var api = new FakeWindowApi();
        using WindowsWindowPlatformProvider provider = Provider(api, out InMemoryWindowEventQueue queue);
        provider.Start(OverlayBounds, VirtualBounds);

        api.Add(Window(10, new RectD(100, 200, 800, 600)), enumerate: false);
        queue.Enqueue(Event(10, WindowChangeKind.Create));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Equal(1, provider.Snapshot.Platforms.Length);

        api.Get(10).DwmBounds = new RectD(250, 200, 700, 600);
        queue.Enqueue(Event(10, WindowChangeKind.MoveOrResize));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Near(2_170, provider.Snapshot.Platforms.Single().Bounds.X);

        queue.Enqueue(Event(10, WindowChangeKind.MinimizeStart));
        queue.Enqueue(Event(10, WindowChangeKind.Destroy));
        provider.Pump(DateTimeOffset.UtcNow, OverlayBounds, VirtualBounds);
        AssertEx.Equal(0, provider.Snapshot.Platforms.Length);
    }

    private static void ReconciliationRecoversMissedEvent()
    {
        var api = new FakeWindowApi();
        using WindowsWindowPlatformProvider provider = Provider(
            api,
            out _,
            TimeSpan.FromSeconds(5));
        DateTimeOffset start = DateTimeOffset.UtcNow;
        provider.Start(OverlayBounds, VirtualBounds);
        api.Add(Window(10, new RectD(100, 200, 800, 600)));
        provider.Pump(start.AddSeconds(6), OverlayBounds, VirtualBounds);
        AssertEx.Equal(1, provider.Snapshot.Platforms.Length);
        AssertEx.Equal(2, api.EnumerationCount);
    }

    private static WindowsWindowPlatformProvider Provider(
        FakeWindowApi api,
        out InMemoryWindowEventQueue queue,
        TimeSpan? interval = null)
    {
        queue = new InMemoryWindowEventQueue();
        return new WindowsWindowPlatformProvider(
            api,
            queue,
            new PlatformRegistry(),
            ownProcessId: 42,
            reconciliationInterval: interval);
    }

    private static FakeWindowData Window(long handle, RectD bounds) => new()
    {
        Handle = handle,
        DwmBounds = bounds,
    };

    private static WindowChangeEvent Event(long handle, WindowChangeKind kind) =>
        new(handle, kind, DateTimeOffset.UtcNow);
}
