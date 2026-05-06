using System.Collections.Concurrent;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public enum GenerationJobStatus { Pending, Running, Completed, Failed }

public class GenerationJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    // volatile int backing field ensures Status writes are release-fenced:
    // any reader who sees Completed/Failed is guaranteed to see Deck/Error too.
    private volatile int _status = (int)GenerationJobStatus.Pending;
    public GenerationJobStatus Status
    {
        get => (GenerationJobStatus)_status;
        set => _status = (int)value;
    }
    public DeckConfiguration? Deck { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string UserId { get; init; } = string.Empty;
}

public class GenerationJobStore
{
    private readonly ConcurrentDictionary<string, GenerationJob> _jobs = new();

    public GenerationJob Create(string userId)
    {
        var job = new GenerationJob { UserId = userId };
        _jobs[job.Id] = job;
        PurgeExpired();
        return job;
    }

    public GenerationJob? Get(string id)
    {
        PurgeExpired();
        return _jobs.TryGetValue(id, out var job) ? job : null;
    }

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var key in _jobs.Keys.ToArray())
        {
            if (_jobs.TryGetValue(key, out var job) && job.CreatedAt < cutoff)
                _jobs.TryRemove(key, out _);
        }
    }
}
