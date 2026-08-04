using System.Text.Json;

namespace DesktopPeople.Core;

/// <summary>
/// Appends one JSON object per line to a dated file. Lives in Core rather than in either host
/// because both of them need it and none of it is UI: when something odd happens on the user's
/// desktop, this file is the only record of what the character was actually doing.
/// </summary>
public sealed class JsonLineLogger : IOverlayLogger
{
    private readonly object _gate = new();
    private readonly string _logPath;

    /// <param name="host">Distinguishes the writer in the file name. The two hosts can be run at
    /// the same time, and pointing both at one file would have them fighting over it — with the
    /// loser's lines silently dropped by the guard below.</param>
    public JsonLineLogger(string logDirectory, string? host = null)
    {
        Directory.CreateDirectory(logDirectory);
        string tag = string.IsNullOrWhiteSpace(host) ? string.Empty : $"-{host}";
        _logPath = Path.Combine(logDirectory, $"desktop-people{tag}-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    /// <summary>Where lines are being written, for hosts that want to tell the user. Not named
    /// <c>Path</c>: that would shadow <see cref="System.IO.Path"/> inside this class.</summary>
    public string FilePath => _logPath;

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
                // runs on the hot path (every frame's window reconciliation), and a host's
                // own unhandled-exception handlers call back into this same method to log
                // whatever just failed — letting the exception escape here risks that handler
                // throwing too, cascading a single locked file into a genuinely fatal,
                // unrecoverable crash instead of one dropped line.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
