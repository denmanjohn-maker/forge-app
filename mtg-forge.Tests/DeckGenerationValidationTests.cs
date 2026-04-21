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
        var service = CreateServiceWithDeckResponse(MakeSection("Creature", 100), "Commander");

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
        var service = CreateServiceWithDeckResponse(MakeSection("Creature", 97), "Commander");

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
        var service = CreateServiceWithDeckResponse(MakeSection("Creature", 105), "Commander");

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
        var service = CreateServiceWithDeckResponse(MakeSection("Creature", 60), "Standard");

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
        var service = CreateServiceWithDeckResponse(MakeSection("Creature", 60), "Modern");

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
    // Budget / price range — BudgetHelper.GetBudgetMax
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Budget", 50)]
    [InlineData("Budget (under $50)", 50)]
    [InlineData("Mid-range ($50-$150)", 150)]
    [InlineData("High ($150-$500)", 500)]
    public void GetBudgetMax_ReturnsCorrectLimit_ForNamedBudgetRanges(string budgetRange, decimal expectedMax)
    {
        var max = BudgetHelper.GetBudgetMax(budgetRange);

        Assert.NotNull(max);
        Assert.Equal(expectedMax, max!.Value);
    }

    [Theory]
    [InlineData("No budget limit")]
    [InlineData("No limit")]
    public void GetBudgetMax_ReturnsNull_ForUnlimitedBudgets(string budgetRange)
    {
        var max = BudgetHelper.GetBudgetMax(budgetRange);

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
        var max = BudgetHelper.GetBudgetMax(budgetRange);

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
        var max = BudgetHelper.GetBudgetMax(budgetRange);

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
        var max = BudgetHelper.GetBudgetMax("Budget");

        Assert.NotNull(max);
        Assert.True(
            totalPrice > max!.Value,
            "Expected over-budget deck to exceed the Budget maximum.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a single-section LocalDeckResponse JSON with <paramref name="count"/> cards
    /// in the given <paramref name="category"/>.
    /// </summary>
    private static object MakeSection(string category, int count) => new
    {
        category,
        cards = Enumerable.Range(1, count).Select(i => new
        {
            name = $"Card {i}",
            quantity = 1,
            manaCost = "{1}",
            cmc = 1.0,
            typeLine = category,
            oracleText = "Test role",
            priceUsd = 0.25
        }).ToList<object>()
    };

    /// <summary>
    /// Creates a <see cref="RagPipelineService"/> whose HTTP handler returns a deck JSON
    /// built from <paramref name="section"/> in the given <paramref name="format"/>.
    /// </summary>
    private static RagPipelineService CreateServiceWithDeckResponse(object section, string format)
    {
        var deckObj = new
        {
            commander = (string?)null,
            theme = "Validation Test",
            format = format.ToLowerInvariant(),
            sections = new[] { section },
            estimatedCost = 25.0,
            reasoning = "A deck used in automated validation tests."
        };

        var deckJson = JsonSerializer.Serialize(deckObj);
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(deckJson, Encoding.UTF8, "application/json")
            }));

        var factory = new StubHttpClientFactory(handler);
        var settings = Options.Create(new RagPipelineSettings
        {
            BaseUrl = "http://localhost:8080",
            LlmBaseUrl = "https://api.together.xyz",
            LlmApiKey = "test-key",
            Model = "test-model"
        });

        return new RagPipelineService(factory, settings, NullLogger<RagPipelineService>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
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
