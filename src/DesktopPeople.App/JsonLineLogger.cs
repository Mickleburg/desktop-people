using System.Text.Json;
using DesktopPeople.Core;

namespace DesktopPeople.App;

internal sealed class JsonLineLogger : IOverlayLogger
{
    private readonly object _gate = new();
    private readonly string _logPath;

    public JsonLineLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"desktop-people-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public void Write(string eventName, object? data = null)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = eventName,
            data,
        };

        string line = JsonSerializer.Serialize(entry);
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log file can be transiently locked by another process (antivirus, a
                // second instance, cloud sync, ...). Losing one line is harmless, but this
                // runs on the UI thread's hot path (every frame's window reconciliation),
                // and the app's own ThreadException/UnhandledException handlers call back
                // into this same method to log whatever just failed — letting the exception
                // escape here risks that handler throwing too, cascading a single locked
                // file into a genuinely fatal, unrecoverable crash instead of one dropped line.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

