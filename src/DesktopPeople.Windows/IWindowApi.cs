using DesktopPeople.Core;

namespace DesktopPeople.Windows;

public interface IWindowApi
{
    IReadOnlyList<nint> EnumerateWindows();

    bool IsWindow(nint handle);

    bool IsWindowVisible(nint handle);

    bool IsIconic(nint handle);

    bool TryGetDwmFrameBounds(nint handle, out RectD bounds);

    bool TryGetWindowRect(nint handle, out RectD bounds);

    long GetStyle(nint handle);

    long GetExtendedStyle(nint handle);

    int GetProcessId(nint handle);

    string GetClassName(nint handle);

    string GetMonitorId(nint handle);

    double GetMonitorTop(nint handle);
}
