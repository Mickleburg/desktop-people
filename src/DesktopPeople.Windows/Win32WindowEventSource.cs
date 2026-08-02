using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DesktopPeople.Windows;

public sealed class Win32WindowEventSource : IWindowEventQueue
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjIdWindow = 0;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private readonly ConcurrentQueue<WindowChangeEvent> _queue = new();
    private readonly NativeMethods.WinEventCallback _callback;
    private readonly List<nint> _hooks = [];
    private bool _disposed;

    public Win32WindowEventSource()
    {
        _callback = OnWindowEvent;
        AddHook(EventSystemForeground, EventSystemForeground);
        AddHook(EventSystemMoveSizeStart, EventSystemMoveSizeEnd);
        AddHook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
        AddHook(EventObjectCreate, EventObjectLocationChange);
    }

    public bool TryDequeue(out WindowChangeEvent windowEvent) => _queue.TryDequeue(out windowEvent);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (nint hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void AddHook(uint minimumEvent, uint maximumEvent)
    {
        nint hook = NativeMethods.SetWinEventHook(
            minimumEvent,
            maximumEvent,
            0,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWindowEvent(
        nint hook,
        uint eventType,
        nint handle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || handle == 0 || childId != 0)
        {
            return;
        }

        if (eventType >= EventObjectCreate && objectId != ObjIdWindow)
        {
            return;
        }

        WindowChangeKind? kind = eventType switch
        {
            EventSystemForeground => WindowChangeKind.ForegroundChanged,
            EventSystemMoveSizeStart => WindowChangeKind.MoveSizeStart,
            EventSystemMoveSizeEnd => WindowChangeKind.MoveSizeEnd,
            EventSystemMinimizeStart => WindowChangeKind.MinimizeStart,
            EventSystemMinimizeEnd => WindowChangeKind.MinimizeEnd,
            EventObjectCreate => WindowChangeKind.Create,
            EventObjectDestroy => WindowChangeKind.Destroy,
            EventObjectShow => WindowChangeKind.Show,
            EventObjectHide => WindowChangeKind.Hide,
            EventObjectLocationChange => WindowChangeKind.MoveOrResize,
            _ => null,
        };
        if (kind is not null)
        {
            _queue.Enqueue(new WindowChangeEvent(
                handle.ToInt64(),
                kind.Value,
                DateTimeOffset.UtcNow));
        }
    }

    private static class NativeMethods
    {
        internal delegate void WinEventCallback(
            nint hook,
            uint eventType,
            nint handle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime);

        [DllImport("user32.dll")]
        internal static extern nint SetWinEventHook(
            uint eventMinimum,
            uint eventMaximum,
            nint eventHookModule,
            WinEventCallback callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(nint eventHook);
    }
}
