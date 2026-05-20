using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

public class CollectionEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string UserId { get; set; } = null!;
    public string CardName { get; set; } = null!;
    public string? SetCode { get; set; }
    public int Quantity { get; set; } = 1;
    public bool Foil { get; set; }
    public string Condition { get; set; } = "NM";
    public decimal EstimatedPrice { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class CollectionUpdateRequest
{
    public int? Quantity { get; set; }
    public string? Condition { get; set; }
    public bool? Foil { get; set; }
    public string? SetCode { get; set; }
}

public class CollectionAddRequest
{
    public string CardName { get; set; } = null!;
    public string? SetCode { get; set; }
    public int Quantity { get; set; } = 1;
    public bool Foil { get; set; }
    public string Condition { get; set; } = "NM";
}

public class OwnershipResult
{
    public int OwnedCount { get; set; }
    public int TotalCards { get; set; }
    public decimal CompletionPct { get; set; }
    public List<MissingCard> MissingCards { get; set; } = new();
    public decimal ShoppingListTotal { get; set; }
}

public class MissingCard
{
    public string Name { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal EstimatedPrice { get; set; }
}

public class BuildableDeck
{
    public string DeckId { get; set; } = null!;
    public string DeckName { get; set; } = null!;
    public string Commander { get; set; } = null!;
    public string Format { get; set; } = null!;
    public List<string> Colors { get; set; } = new();
    public int TotalCards { get; set; }
    public int OwnedCount { get; set; }
    public decimal CompletionPct { get; set; }
    public int MissingCount { get; set; }
    public decimal AcquisitionCost { get; set; }
}
