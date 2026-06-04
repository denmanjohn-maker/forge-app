using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace MtgForge.Api.Models;

public class AiChatSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? DeckId { get; set; }

    public List<AiChatMessage> Messages { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AiChatAction
{
    public string Type { get; set; } = null!; // "add", "remove", "swap", "reply"
    public string? RemoveCard { get; set; }
    public string? AddCard { get; set; }
    public string? Message { get; set; }
    public string Label { get; set; } = null!;
}

public class AiChatMessage
{
    /// <summary>
    /// e.g. "user", "assistant", "system"
    /// </summary>
    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;
    
    public List<AiChatAction>? Actions { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AiBrewRequest
{
    public string? SessionId { get; set; }
    public string? DeckId { get; set; }
    public string Prompt { get; set; } = null!;
}
