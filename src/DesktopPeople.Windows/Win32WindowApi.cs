using System.Runtime.InteropServices;
using System.Text;
using DesktopPeople.Core;

namespace DesktopPeople.Windows;

public sealed class Win32WindowApi : IWindowApi
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint MonitorDefaultToNearest = 2;

    public IReadOnlyList<nint> EnumerateWindows()
    {
        var handles = new List<nint>();
        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                handles.Add(handle);
                return true;
            },
            0);
        return handles;
    }

    public bool IsWindow(nint handle) => NativeMethods.IsWindow(handle);

    public bool IsWindowVisible(nint handle) => NativeMethods.IsWindowVisible(handle);

    public bool IsIconic(nint handle) => NativeMethods.IsIconic(handle);

    public bool TryGetDwmFrameBounds(nint handle, out RectD bounds)
    {
        int result = NativeMethods.DwmGetWindowAttribute(
            handle,
            DwmwaExtendedFrameBounds,
            out NativeRect rectangle,
            Marshal.SizeOf<NativeRect>());
        bounds = result == 0 ? rectangle.ToRectD() : default;
        return result == 0 && bounds.Width > 0 && bounds.Height > 0;
    }

    public bool TryGetWindowRect(nint handle, out RectD bounds)
    {
        bool success = NativeMethods.GetWindowRect(handle, out NativeRect rectangle);
        bounds = success ? rectangle.ToRectD() : default;
        return success && bounds.Width > 0 && bounds.Height > 0;
    }

    public long GetStyle(nint handle) => NativeMethods.GetWindowLongPtr(handle, GwlStyle).ToInt64();

    public long GetExtendedStyle(nint handle) =>
        NativeMethods.GetWindowLongPtr(handle, GwlExStyle).ToInt64();

    public int GetProcessId(nint handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        return unchecked((int)processId);
    }

    public string GetClassName(nint handle)
    {
        var builder = new StringBuilder(256);
        int length = NativeMethods.GetClassName(handle, builder, builder.Capacity);
        return length > 0 ? builder.ToString(0, length) : string.Empty;
    }

    public string GetMonitorId(nint handle)
    {
        nint monitor = NativeMethods.MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return string.Empty;
        }

        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info)
            ? $"monitor:{monitor.ToInt64():X}"
            : string.Empty;
    }

    public double GetMonitorTop(nint handle)
    {
        nint monitor = NativeMethods.MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return double.NegativeInfinity;
        }

        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info)
            ? info.Monitor.Top
            : double.NegativeInfinity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly RectD ToRectD() =>
            new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsCallback(nint handle, nint parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint handle, out NativeRect rectangle);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            nint handle,
            int attribute,
            out NativeRect value,
            int valueSize);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(nint handle, StringBuilder value, int maximumCount);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromWindow(nint handle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);
    }
}
