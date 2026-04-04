using System.Collections.Concurrent;
using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace MtgDeckForge.Api.Observability;

/// <summary>
/// Thread-safe ring buffer that stores the last N structured log entries.
/// Used by the /logging endpoint to expose recent logs.
/// </summary>
public sealed class InMemoryLogStore
{
    private readonly LogEntry[] _buffer;
    private int _index;
    private readonly object _lock = new();

    public InMemoryLogStore(int capacity = 1000)
    {
        _buffer = new LogEntry[capacity];
    }

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _buffer[_index % _buffer.Length] = entry;
            _index++;
        }
    }

    public List<LogEntry> GetRecent(int count = 200)
    {
        lock (_lock)
        {
            var total = Math.Min(_index, _buffer.Length);
            var take = Math.Min(count, total);
            var result = new List<LogEntry>(take);
            var start = _index - take;
            if (start < 0) start = 0;

            for (var i = start; i < _index; i++)
                result.Add(_buffer[i % _buffer.Length]);

            result.Reverse(); // most recent first
            return result;
        }
    }
}

public record LogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    Dictionary<string, object?>? Properties);

/// <summary>
/// Serilog sink that writes to the InMemoryLogStore ring buffer.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly InMemoryLogStore _store;

    public InMemoryLogSink(InMemoryLogStore store) => _store = store;

    public void Emit(LogEvent logEvent)
    {
        var props = new Dictionary<string, object?>();
        foreach (var kvp in logEvent.Properties)
        {
            props[kvp.Key] = kvp.Value switch
            {
                ScalarValue sv => sv.Value,
                SequenceValue seq => seq.Elements.Select(e => e.ToString()).ToList(),
                _ => kvp.Value.ToString()
            };
        }

        _store.Add(new LogEntry(
            logEvent.Timestamp.UtcDateTime,
            logEvent.Level.ToString(),
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString(),
            props));
    }
}
