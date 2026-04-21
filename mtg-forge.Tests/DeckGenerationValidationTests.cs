using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Tests;

/// <summary>
/// Validates that deck generation enforces the correct card count for each format
/// and that the deck's total price is consistent with the requested budget range.
/// </summary>
public class DeckGenerationValidationTests
{
    // -------------------------------------------------------------------------
    // Card count — Commander format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateDeckAsync_CommanderFormat_RetainsExactly100Cards_WhenApiReturnsExact100()
    {
        var service = CreateServiceWithDeckResponse(MakeCards(100, "Creature"), "Commander");

        var result = await service.GenerateDeckAsync(new DeckGenerationRequest
        {
            Colors = ["G"],
            Format = "Commander",
            BudgetRange = "Budget"
        });

        Assert.Equal(100, result.TotalCards);
        Assert.Equal(100, result.Cards.Sum(c => c.Quantity));
    }

    [Fact]
    public async Task GenerateDeckAsync_CommanderFormat_Pads_To100Cards_WhenApiReturnsTooFew()
    {
        // API returns 97 cards → service must pad to exactly 100 with basic lands
        var service = CreateServiceWithDeckResponse(MakeCards(97, "Creature"), "Commander");

        var result = await service.GenerateDeckAsync(new DeckGenerationRequest
        {
            Colors = ["G"],
            Format = "Commander",
            BudgetRange = "Budget"
        });

        Assert.Equal(100, result.TotalCards);
        Assert.Equal(100, result.Cards.Sum(c => c.Quantity));
    }

    [Fact]
    public async Task GenerateDeckAsync_CommanderFormat_Trims_To100Cards_WhenApiReturnsTooMany()
    {
        // API returns 105 non-land non-commander cards → service must trim to exactly 100
        var service = CreateServiceWithDeckResponse(MakeCards(105, "Creature"), "Commander");

        var result = await service.GenerateDeckAsync(new DeckGenerationRequest
        {
            Colors = ["G"],
            Format = "Commander",
            BudgetRange = "Budget"
        });

        Assert.Equal(100, result.TotalCards);
        Assert.Equal(100, result.Cards.Sum(c => c.Quantity));
    }

    // -------------------------------------------------------------------------
    // Card count — non-Commander format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateDeckAsync_StandardFormat_PreservesApiCardCount()
    {
        // For non-Commander formats the service does not modify the card list,
        // so TotalCards should match whatever the API returned.
        var service = CreateServiceWithDeckResponse(MakeCards(60, "Creature"), "Standard");

        var result = await service.GenerateDeckAsync(new DeckGenerationRequest
        {
            Colors = ["G"],
            Format = "Standard",
            BudgetRange = "Budget"
        });

        Assert.Equal(60, result.TotalCards);
        Assert.Equal(60, result.Cards.Sum(c => c.Quantity));
    }

    [Fact]
    public async Task GenerateDeckAsync_ModernFormat_PreservesApiCardCount()
    {
        var service = CreateServiceWithDeckResponse(MakeCards(60, "Creature"), "Modern");

        var result = await service.GenerateDeckAsync(new DeckGenerationRequest
        {
            Colors = ["U", "B"],
            Format = "Modern",
            BudgetRange = "Budget"
        });

        Assert.Equal(60, result.TotalCards);
        Assert.Equal(60, result.Cards.Sum(c => c.Quantity));
    }

    // -------------------------------------------------------------------------
    // Budget / price range — GetBudgetMax
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Budget", 50)]
    [InlineData("Budget (under $50)", 50)]
    [InlineData("Mid-range ($50-$150)", 150)]
    [InlineData("High ($150-$500)", 500)]
    public void GetBudgetMax_ReturnsCorrectLimit_ForNamedBudgetRanges(string budgetRange, decimal expectedMax)
    {
        var max = ClaudeService.GetBudgetMax(budgetRange);

        Assert.NotNull(max);
        Assert.Equal(expectedMax, max!.Value);
    }

    [Theory]
    [InlineData("No budget limit")]
    [InlineData("No limit")]
    public void GetBudgetMax_ReturnsNull_ForUnlimitedBudgets(string budgetRange)
    {
        var max = ClaudeService.GetBudgetMax(budgetRange);

        Assert.Null(max);
    }

    // -------------------------------------------------------------------------
    // Deck total price — fits within each budget tier
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Budget", 50)]
    [InlineData("Mid-range ($50-$150)", 150)]
    [InlineData("High ($150-$500)", 500)]
    public void DeckTotalPrice_IsWithinBudget_WhenAllCardPricesAreWithinLimit(
        string budgetRange,
        decimal expectedMax)
    {
        // Build a 100-card deck priced at $0.25 each → total $25, within all tiers.
        var cards = Enumerable.Range(1, 100).Select(i => new CardEntry
        {
            Name = $"Card {i}",
            Quantity = 1,
            EstimatedPrice = 0.25m
        }).ToList();

        var totalPrice = cards.Sum(c => c.EstimatedPrice * c.Quantity);
        var max = ClaudeService.GetBudgetMax(budgetRange);

        Assert.NotNull(max);
        Assert.Equal(expectedMax, max!.Value);
        Assert.True(
            totalPrice <= max!.Value,
            $"Total price ${totalPrice:F2} exceeds budget maximum ${max.Value:F2} for range '{budgetRange}'.");
    }

    [Theory]
    [InlineData("Budget", 50, 0.49)]           // 100 cards × $0.49 = $49 ≤ $50
    [InlineData("Mid-range ($50-$150)", 150, 1.49)] // 100 cards × $1.49 = $149 ≤ $150
    [InlineData("High ($150-$500)", 500, 4.99)]    // 100 cards × $4.99 = $499 ≤ $500
    public void DeckTotalPrice_AtBudgetCeiling_IsStillWithinBudget(
        string budgetRange,
        decimal expectedMax,
        double pricePerCard)
    {
        var cards = Enumerable.Range(1, 100).Select(i => new CardEntry
        {
            Name = $"Card {i}",
            Quantity = 1,
            EstimatedPrice = (decimal)pricePerCard
        }).ToList();

        var totalPrice = cards.Sum(c => c.EstimatedPrice * c.Quantity);
        var max = ClaudeService.GetBudgetMax(budgetRange);

        Assert.NotNull(max);
        Assert.Equal(expectedMax, max!.Value);
        Assert.True(
            totalPrice <= max!.Value,
            $"Total price ${totalPrice:F2} exceeds budget maximum ${max.Value:F2} for range '{budgetRange}'.");
    }

    [Fact]
    public void DeckTotalPrice_ExceedsMax_WhenCardPricesExceedBudget()
    {
        // Sanity check: a deck priced at $5 per card × 100 cards = $500 is NOT within "Budget" ($50)
        var cards = Enumerable.Range(1, 100).Select(i => new CardEntry
        {
            Name = $"Card {i}",
            Quantity = 1,
            EstimatedPrice = 5.00m
        }).ToList();

        var totalPrice = cards.Sum(c => c.EstimatedPrice * c.Quantity);
        var max = ClaudeService.GetBudgetMax("Budget");

        Assert.NotNull(max);
        Assert.True(
            totalPrice > max!.Value,
            "Expected over-budget deck to exceed the Budget maximum.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Builds a list of <paramref name="count"/> CardEntry-compatible anonymous objects.</summary>
    private static List<object> MakeCards(int count, string category) =>
        Enumerable.Range(1, count).Select(i => (object)new
        {
            name = $"Card {i}",
            quantity = 1,
            manaCost = "{1}",
            cmc = 1,
            cardType = category,
            category,
            roleInDeck = "Test role",
            estimatedPrice = 0.25
        }).ToList();

    /// <summary>
    /// Creates a <see cref="ClaudeService"/> whose HTTP handler returns a deck JSON
    /// built from <paramref name="cards"/> in the given <paramref name="format"/>.
    /// </summary>
    private static ClaudeService CreateServiceWithDeckResponse(List<object> cards, string format)
    {
        var deckObj = new
        {
            deckName = "Validation Test Deck",
            commander = "Test Commander",
            strategy = "Test strategy for validation.",
            estimatedTotalPrice = cards.Count * 0.25,
            totalCards = cards.Count,
            deckDescription = "A deck used in automated validation tests.",
            cards
        };

        // Serialize deck → embed as JSON string in a Claude API response envelope
        var deckJson = JsonSerializer.Serialize(deckObj);
        var textFieldJson = JsonSerializer.Serialize(deckJson); // produces a JSON-encoded string literal
        var claudeApiResponse = $$"""{"content":[{"type":"text","text":{{textFieldJson}}}]}""";

        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(claudeApiResponse, Encoding.UTF8, "application/json")
            }));

        var httpClient = new HttpClient(handler);
        var settings = Options.Create(new ClaudeApiSettings
        {
            ApiKey = "test-key",
            Model = "test-model",
            MaxTokens = 8192
        });

        return new ClaudeService(httpClient, settings, NullLogger<ClaudeService>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handlerFunc(request);
    }
}
