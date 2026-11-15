using System.Text;

namespace TorrentFree.Services;

/// <summary>Local, bounded diagnostics. Breadcrumbs contain event/state names, never activation payloads.</summary>
internal sealed class DiagnosticLog(string path, string environment)
{
    private readonly BoundedCrashLog _file = new(path);
    private readonly Queue<string> _events = new();
    private readonly object _gate = new();
    private readonly string _session = Guid.NewGuid().ToString("N");

    public void Breadcrumb(string state)
    {
        lock (_gate)
        {
            if (_events.Count == 32) _events.Dequeue();
            _events.Enqueue($"{DateTimeOffset.UtcNow:O} thread={Environment.CurrentManagedThreadId} {state}");
        }
    }

    public void Write(string source, Exception? exception = null, string? details = null)
    {
        try
        {
            var entry = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.UtcNow:O}] {source} session={_session} thread={Environment.CurrentManagedThreadId}")
                .AppendLine(environment);
            lock (_gate)
                foreach (var state in _events) entry.AppendLine(state);
            if (exception is not null)
                entry.AppendLine($"HRESULT=0x{exception.HResult:X8}").AppendLine(exception.ToString());
            if (!string.IsNullOrWhiteSpace(details)) entry.AppendLine(details);
            entry.AppendLine("---");
            _file.Append(entry.ToString());
        }
        catch { /* Diagnostics must not replace the original exception or prevent shutdown. */ }
    }
}
