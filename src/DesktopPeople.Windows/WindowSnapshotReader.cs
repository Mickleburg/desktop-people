using System.Runtime.InteropServices;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.Windows;

public sealed class WindowSnapshotReader
{
    private readonly IWindowApi _api;

    public WindowSnapshotReader(IWindowApi api)
    {
        _api = api;
    }

    public IReadOnlyList<WindowCandidate> ReadAll()
    {
        IReadOnlyList<nint> handles;
        try
        {
            handles = _api.EnumerateWindows();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return [];
        }

        var result = new List<WindowCandidate>(handles.Count);
        for (int index = 0; index < handles.Count; index++)
        {
            result.Add(Read(handles[index], index));
        }

        return result;
    }

    public WindowCandidate Read(nint handle, int zOrder)
    {
        try
        {
            if (handle == 0 || !_api.IsWindow(handle))
            {
                return Invalid(handle, zOrder);
            }

            bool usedDwmBounds = _api.TryGetDwmFrameBounds(handle, out RectD bounds);
            if (!usedDwmBounds && !_api.TryGetWindowRect(handle, out bounds))
            {
                return Invalid(handle, zOrder);
            }

            return new WindowCandidate
            {
                Handle = handle.ToInt64(),
                IsValid = true,
                IsVisible = _api.IsWindowVisible(handle),
                IsMinimized = _api.IsIconic(handle),
                ScreenBounds = bounds,
                UsedDwmBounds = usedDwmBounds,
                Style = _api.GetStyle(handle),
                ExtendedStyle = _api.GetExtendedStyle(handle),
                ProcessId = _api.GetProcessId(handle),
                ClassName = _api.GetClassName(handle),
                ZOrder = zOrder,
                MonitorId = _api.GetMonitorId(handle),
            };
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Invalid(handle, zOrder);
        }
    }

    private static WindowCandidate Invalid(nint handle, int zOrder) => new()
    {
        Handle = handle.ToInt64(),
        IsValid = false,
        ScreenBounds = default,
        ZOrder = zOrder,
    };

    private static bool IsRecoverable(Exception exception) =>
        exception is InvalidOperationException or ExternalException or UnauthorizedAccessException;
}
