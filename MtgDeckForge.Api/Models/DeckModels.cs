using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgDeckForge.Api.Models;

public class DeckConfiguration
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string DeckName { get; set; } = null!;
    public string Commander { get; set; } = null!;
    public string Strategy { get; set; } = null!;
    public string Format { get; set; } = "Commander";
    public List<string> Colors { get; set; } = new();
    public string PowerLevel { get; set; } = "Casual";
    public string BudgetRange { get; set; } = "Budget";
    public decimal EstimatedTotalPrice { get; set; }
    public int TotalCards { get; set; }
    public string DeckDescription { get; set; } = null!;
    public List<CardEntry> Cards { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CardEntry
{
    public string Name { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public string ManaCost { get; set; } = string.Empty;
    public int Cmc { get; set; }
    public string CardType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string RoleInDeck { get; set; } = null!;
    public decimal EstimatedPrice { get; set; }
}

public class DeckGenerationRequest
{
    public List<string> Colors { get; set; } = new();
    public string Format { get; set; } = "Commander";
    public string PowerLevel { get; set; } = "Casual";
    public string BudgetRange { get; set; } = "Budget";
    public string? PreferredStrategy { get; set; }
    public string? PreferredCommander { get; set; }
    public string? AdditionalNotes { get; set; }
}
