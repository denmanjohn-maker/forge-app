using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

public interface IDeckGenerationService
{
    Task<DeckConfiguration> GenerateDeckAsync(DeckGenerationRequest request);
    Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck);
    Task<List<CardEntry>> SuggestBudgetReplacementsAsync(
        DeckConfiguration deck,
        List<CardEntry> expensiveCards,
        decimal currentTotal,
        decimal budgetMax,
        List<(string CardName, decimal Price)> cheapCardPool);
    Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards);
}
