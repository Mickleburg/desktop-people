using System.Text.Json;

namespace DesktopPeople.App;

internal sealed class JsonLineLogger
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
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }
}

