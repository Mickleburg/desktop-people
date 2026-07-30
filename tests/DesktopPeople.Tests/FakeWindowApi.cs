using DesktopPeople.Core;
using DesktopPeople.Windows;

namespace DesktopPeople.Tests;

internal sealed class FakeWindowApi : IWindowApi
{
    private readonly Dictionary<long, FakeWindowData> _windows = [];
    private readonly List<long> _zOrder = [];

    public int EnumerationCount { get; private set; }

    public int DwmBoundsCallCount { get; private set; }

    public int WindowRectCallCount { get; private set; }

    public void Add(FakeWindowData window, bool enumerate = true)
    {
        _windows[window.Handle] = window;
        if (enumerate && !_zOrder.Contains(window.Handle))
        {
            _zOrder.Add(window.Handle);
        }
    }

    public FakeWindowData Get(long handle) => _windows[handle];

    public void Remove(long handle)
    {
        _windows.Remove(handle);
        _zOrder.Remove(handle);
    }

    public IReadOnlyList<nint> EnumerateWindows()
    {
        EnumerationCount++;
        return _zOrder.Select(handle => new nint(handle)).ToArray();
    }

    public bool IsWindow(nint handle) =>
        _windows.TryGetValue(handle.ToInt64(), out FakeWindowData? window) && window.IsValid;

    public bool IsWindowVisible(nint handle) => Get(handle.ToInt64()).IsVisible;

    public bool IsIconic(nint handle) => Get(handle.ToInt64()).IsMinimized;

    public bool TryGetDwmFrameBounds(nint handle, out RectD bounds)
    {
        DwmBoundsCallCount++;
        FakeWindowData window = Get(handle.ToInt64());
        if (window.ThrowOnRead)
        {
            throw new InvalidOperationException("Window disappeared.");
        }

        bounds = window.DwmBounds ?? default;
        return window.DwmBounds is not null;
    }

    public bool TryGetWindowRect(nint handle, out RectD bounds)
    {
        WindowRectCallCount++;
        FakeWindowData window = Get(handle.ToInt64());
        bounds = window.WindowBounds;
        return window.WindowBounds.Width > 0 && window.WindowBounds.Height > 0;
    }

    public long GetStyle(nint handle) => Get(handle.ToInt64()).Style;

    public long GetExtendedStyle(nint handle) => Get(handle.ToInt64()).ExtendedStyle;

    public int GetProcessId(nint handle) => Get(handle.ToInt64()).ProcessId;

    public string GetClassName(nint handle) => Get(handle.ToInt64()).ClassName;

    public string GetMonitorId(nint handle) => Get(handle.ToInt64()).MonitorId;
}

internal sealed class FakeWindowData
{
    public long Handle { get; init; }

    public bool IsValid { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    public bool IsMinimized { get; set; }

    public RectD? DwmBounds { get; set; }

    public RectD WindowBounds { get; set; } = new(100, 100, 800, 600);

    public long Style { get; set; }

    public long ExtendedStyle { get; set; }

    public int ProcessId { get; set; } = 99;

    public string ClassName { get; set; } = "Notepad";

    public string MonitorId { get; set; } = "monitor:1";

    public bool ThrowOnRead { get; set; }
}
