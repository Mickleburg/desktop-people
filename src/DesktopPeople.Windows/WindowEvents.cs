using System.Collections.Concurrent;

namespace DesktopPeople.Windows;

public enum WindowChangeKind
{
    Create,
    Destroy,
    Show,
    Hide,
    MoveOrResize,
    MoveSizeStart,
    MoveSizeEnd,
    MinimizeStart,
    MinimizeEnd,
}

public readonly record struct WindowChangeEvent(
    long Handle,
    WindowChangeKind Kind,
    DateTimeOffset OccurredAt);

public interface IWindowEventQueue : IDisposable
{
    bool TryDequeue(out WindowChangeEvent windowEvent);
}

public sealed class InMemoryWindowEventQueue : IWindowEventQueue
{
    private readonly ConcurrentQueue<WindowChangeEvent> _queue = new();

    public void Enqueue(WindowChangeEvent windowEvent) => _queue.Enqueue(windowEvent);

    public bool TryDequeue(out WindowChangeEvent windowEvent) => _queue.TryDequeue(out windowEvent);

    public void Dispose()
    {
    }
}
