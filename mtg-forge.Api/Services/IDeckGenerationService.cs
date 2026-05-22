using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Abstraction over the AI deck generation pipeline.
/// The only registered implementation is <see cref="RagPipelineService"/>, which
/// proxies generation to forge-ai-api (Qdrant RAG + DeepInfra LLM) and calls
/// DeepInfra directly for analysis and auxiliary operations.
/// </summary>
public interface IDeckGenerationService
{
    /// <summary>
    /// Generates a complete deck from a <see cref="DeckGenerationRequest"/>.
    /// Returns a <see cref="DeckConfiguration"/> populated with cards, metadata, and
    /// an estimated total price, but does not persist it to the database.
    /// </summary>
    Task<DeckConfiguration> GenerateDeckAsync(DeckGenerationRequest request);

    /// <summary>
    /// Analyzes an existing deck and returns strengths, weaknesses, improvement
    /// suggestions, and concrete card-swap recommendations.
    /// </summary>
    Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck);

    /// <summary>
    /// Given a list of over-budget cards and a pool of cheap alternatives, asks the
    /// LLM to select suitable replacements that keep the deck playable.
    /// Returns the replacement <see cref="CardEntry"/> objects in the same order as
    /// <paramref name="expensiveCards"/>.
    /// </summary>
    Task<List<CardEntry>> SuggestBudgetReplacementsAsync(
        DeckConfiguration deck,
        List<CardEntry> expensiveCards,
        decimal currentTotal,
        decimal budgetMax,
        List<(string CardName, decimal Price)> cheapCardPool);

    /// <summary>
    /// Generates a short, flavor-rich description for a deck that was imported
    /// from an external CSV rather than AI-generated. Used to populate
    /// <see cref="DeckConfiguration.DeckDescription"/> on import.
    /// </summary>
    Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards);

    /// <summary>
    /// Returns a list of card recommendations that would strengthen the given deck,
    /// taking into account its strategy, colors, format, and budget.
    /// </summary>
    Task<List<CardRecommendation>> GetCardRecommendationsAsync(DeckConfiguration deck);
}
