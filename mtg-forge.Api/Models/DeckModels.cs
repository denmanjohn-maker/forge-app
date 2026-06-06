using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

/// <summary>
/// Root document stored in the MongoDB <c>decks</c> collection representing a
/// complete MTG deck — its cards, metadata, AI analysis, and ownership information.
/// </summary>
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
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Persisted analysis
    public DeckAnalysis? LastAnalysis { get; set; }
    public DateTime? LastAnalyzedAt { get; set; }

    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; } = false;

    // Deck primer / strategy guide (Markdown)
    public string? Primer { get; set; }
}

/// <summary>
/// Represents a single card slot in a deck, including quantity, mana cost,
/// card type, AI-assigned category (e.g. "Ramp", "Removal"), role description,
/// and estimated price.
/// </summary>
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

/// <summary>
/// Payload for the <c>POST /api/decks/generate</c> endpoint. Specifies the desired
/// colors, format, power level, budget, strategy, commander preference, and any
/// free-form notes (including Universes Beyond themed set references).
/// </summary>
public class DeckGenerationRequest
{
    public List<string> Colors { get; set; } = new();
    public string Format { get; set; } = "Commander";
    public string PowerLevel { get; set; } = "Casual";
    public string BudgetRange { get; set; } = "Budget";
    public string? PreferredStrategy { get; set; }
    public string? PreferredCommander { get; set; }
    public string? AdditionalNotes { get; set; }
    /// <summary>Optional tribe/archetype hints (e.g. ["Elves", "Aggro"]) appended to extraContext.</summary>
    public List<string>? ThemeHints { get; set; }
}

/// <summary>Payload for <c>POST /api/decks/{id}/refine</c>.</summary>
public class DeckRefinementRequest
{
    public string RefinementPrompt { get; set; } = null!;
}

/// <summary>Payload for <c>POST /api/decks/{id}/optimize-budget</c>.</summary>
public class OptimizeBudgetRequest
{
    public decimal TargetBudget { get; set; }
}

/// <summary>
/// Partial-update payload for <c>PATCH /api/decks/{id}</c>. Only non-null fields
/// are applied — omitted fields are left unchanged.
/// </summary>
public class DeckUpdateRequest
{
    public string? DeckName { get; set; }
    public string? Commander { get; set; }
    public string? Strategy { get; set; }
    public string? DeckDescription { get; set; }
    public string? Format { get; set; }
    public string? PowerLevel { get; set; }
    public string? BudgetRange { get; set; }
    public List<string>? Colors { get; set; }
    public List<CardEntry>? Cards { get; set; }
    public List<string>? Tags { get; set; }
    public bool? IsFavorite { get; set; }
    public string? Primer { get; set; }
}

/// <summary>
/// AI-generated analysis of a deck's strengths, weaknesses, and suggested improvements,
/// returned by <c>POST /api/decks/{id}/analyze</c> and persisted to the deck document.
/// </summary>
public class DeckAnalysis
{
    public string SynergyAssessment { get; set; } = null!;
    public string OverallRating { get; set; } = null!;
    public string? Primer { get; set; }
    public List<string> Weaknesses { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = new();
    public List<CardUpgrade> CardUpgrades { get; set; } = new();
}

/// <summary>A suggested one-for-one card swap, part of a <see cref="DeckAnalysis"/>.</summary>
public class CardUpgrade
{
    public string RemoveCard { get; set; } = null!;
    public string AddCard { get; set; } = null!;
    public string Reason { get; set; } = null!;
}

/// <summary>
/// Generic wrapper for paginated list responses. <see cref="HasMore"/> is
/// <c>true</c> when additional pages exist beyond the current result set.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
    public bool HasMore => Skip + Items.Count < Total;
}

/// <summary>
/// Snapshot of a deck's card list at a point in time, written whenever cards are
/// added, removed, or their quantities change via <c>PATCH /api/decks/{id}</c>.
/// </summary>
public class DeckHistoryEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string DeckId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ChangeSummary { get; set; } = null!;
    public List<string> CardsAdded { get; set; } = new();
    public List<string> CardsRemoved { get; set; } = new();
}

/// <summary>
/// An AI-generated card recommendation for an existing deck, returned by
/// <c>GET /api/decks/{id}/recommendations</c>.
/// </summary>
public class CardRecommendation
{
    public string Name { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string EstimatedBudgetTier { get; set; } = "mid";
    public bool IsOwned { get; set; }
    public decimal EstimatedPrice { get; set; }
}

/// <summary>Payload for <c>POST /api/decks/{id}/add-card</c>.</summary>
public class AddCardRequest
{
    public string CardName { get; set; } = null!;
    public string? Category { get; set; }
}
