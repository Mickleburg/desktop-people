namespace DesktopPeople.Core;

/// <summary>Structured logging seam. The simulation records the same behavioural events it
/// always did (state changes, platform loss, recovery from corrupted state), but must not
/// know how or where they are written — the WinForms host and the Godot host each supply
/// their own writer.</summary>
public interface IOverlayLogger
{
    void Write(string eventName, object? data = null);
}

/// <summary>Drops every event. Useful for tests that exercise behaviour rather than logging.</summary>
public sealed class NullOverlayLogger : IOverlayLogger
{
    public static NullOverlayLogger Instance { get; } = new();

    public void Write(string eventName, object? data = null)
    {
    }
}
