using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public enum GenerationJobStatus { Pending, Running, Completed, Failed }

public class GenerationJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
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
    private readonly IMongoCollection<GenerationJobDocument> _jobsCollection;

    public GenerationJobStore(IOptions<MongoDbSettings> settings)
    {
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _jobsCollection = mongoDatabase.GetCollection<GenerationJobDocument>(settings.Value.JobsCollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var indexModel = new CreateIndexModel<GenerationJobDocument>(
            Builders<GenerationJobDocument>.IndexKeys.Descending(j => j.CreatedAt));
        _jobsCollection.Indexes.CreateOne(indexModel);
    }

    public GenerationJob Create(string userId)
    {
        var doc = new GenerationJobDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Status = GenerationJobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _jobsCollection.InsertOne(doc);
        return ToJob(doc);
    }

    public GenerationJob? Get(string id)
    {
        PurgeExpired();
        var doc = _jobsCollection.Find(j => j.Id == id).FirstOrDefault();
        return doc is null ? null : ToJob(doc);
    }

    public void Update(string id, GenerationJobStatus status, DeckConfiguration? deck = null, string? error = null)
    {
        var update = Builders<GenerationJobDocument>.Update
            .Set(j => j.Status, status)
            .Set(j => j.UpdatedAt, DateTime.UtcNow);

        if (deck is not null)
            update = update.Set(j => j.Deck, deck);
        if (error is not null)
            update = update.Set(j => j.Error, error);

        _jobsCollection.UpdateOne(j => j.Id == id, update);
    }

    private static GenerationJob ToJob(GenerationJobDocument doc) => new()
    {
        Id = doc.Id!,
        UserId = doc.UserId,
        Status = doc.Status,
        Deck = doc.Deck,
        Error = doc.Error,
        CreatedAt = doc.CreatedAt
    };

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        _jobsCollection.DeleteMany(j => j.CreatedAt < cutoff && j.Status != GenerationJobStatus.Running);
    }
}
