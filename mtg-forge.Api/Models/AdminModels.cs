using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

public class DeckAnalyticsResult
{
    public int TotalDecks { get; set; }
    public int DecksLast7Days { get; set; }
    public int DecksLast30Days { get; set; }
    public Dictionary<string, int> ByFormat { get; set; } = new();
    public Dictionary<string, int> ByColor { get; set; } = new();
    public Dictionary<string, int> ByPowerLevel { get; set; } = new();
    public Dictionary<string, int> ByBudget { get; set; } = new();
    public List<UserDeckCount> TopUsers { get; set; } = new();
}

public class UserDeckCount
{
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
}

public class AiUsageRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? DeckId { get; set; }
    public string? Format { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AiUsageSummary
{
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public List<UserUsageSummary> ByUser { get; set; } = new();
}

public class UserUsageSummary
{
    public string DisplayName { get; set; } = "";
    public int GenerateCount { get; set; }
    public int AnalyzeCount { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
}
