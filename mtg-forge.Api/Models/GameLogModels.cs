using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

public class GameLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string DeckId { get; set; } = null!;

    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// "win", "loss", or "draw"
    /// </summary>
    public string Result { get; set; } = null!;

    public string? OpponentArchetype { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OpponentDeckId { get; set; }

    public string? Notes { get; set; }

    public string Format { get; set; } = "Commander";

    public int? GameNumber { get; set; }
    public int? TurnCount { get; set; }
    public int? MulliganCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request body for creating a game log. Only contains client-supplied fields;
/// UserId, Id, and Date are assigned server-side. Keeping UserId off the bound
/// model avoids the implicit [Required] validation failure (non-nullable
/// reference type) that would otherwise reject requests with HTTP 400, and
/// prevents callers from spoofing ownership.
/// </summary>
public class CreateGameLogRequest
{
    public string DeckId { get; set; } = null!;

    /// <summary>
    /// "win", "loss", or "draw"
    /// </summary>
    public string Result { get; set; } = null!;

    public string? OpponentArchetype { get; set; }

    public string? OpponentDeckId { get; set; }

    public string? Notes { get; set; }

    public string? Format { get; set; }

    public int? GameNumber { get; set; }
    public int? TurnCount { get; set; }
    public int? MulliganCount { get; set; }
}

public class DeckWinRateStats
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string DeckId { get; set; } = null!;

    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public double WinRate { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
