using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MtgForge.Api.Services;

namespace MtgForge.Api.Models;

/// <summary>
/// Persisted representation of a deck generation job stored in MongoDB.
/// Survives container restarts and allows status polling across instances.
/// </summary>
public class GenerationJobDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
    public DeckConfiguration? Deck { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
