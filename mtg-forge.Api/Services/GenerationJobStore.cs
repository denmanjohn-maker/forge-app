using System.Collections.Concurrent;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public enum GenerationJobStatus { Pending, Running, Completed, Failed }

public class GenerationJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
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

    public GenerationJob? Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var key in _jobs.Keys.ToArray())
        {
            if (_jobs.TryGetValue(key, out var job)
                && job.CreatedAt < cutoff
                && job.Status is GenerationJobStatus.Completed or GenerationJobStatus.Failed)
            {
                _jobs.TryRemove(key, out _);
            }
        }
    }
}
