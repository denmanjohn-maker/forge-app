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
/// Tests for the AI card recommendation pipeline:
/// - BuildRecommendationQuery helper
/// - Qdrant-grounded two-step recommendation flow (GetCardRecommendationsAsync)
/// </summary>
public class CardRecommendationTests
{
    // ─── BuildRecommendationQuery ─────────────────────────────────────────────

    [Fact]
    public void BuildRecommendationQuery_IncludesCommanderAndStrategy()
    {
        var deck = new DeckConfiguration
        {
            Commander = "Atraxa, Praetors' Voice",
            Strategy = "Proliferate Superfriends",
            Cards = []
        };

        var query = RagPipelineService.BuildRecommendationQuery(deck);

        Assert.Contains("Atraxa, Praetors' Voice", query);
        Assert.Contains("Proliferate Superfriends", query);
    }

    [Fact]
    public void BuildRecommendationQuery_ExcludesLandAndCommanderCards()
    {
        var deck = new DeckConfiguration
        {
            Commander = "Atraxa, Praetors' Voice",
            Strategy = "Proliferate",
            Cards =
            [
                new CardEntry { Name = "Forest", CardType = "Basic Land", Category = "Land" },
                new CardEntry { Name = "Atraxa, Praetors' Voice", Category = "Commander" },
                new CardEntry { Name = "Doubling Season", CardType = "Enchantment", Category = "Enchantment" },
                new CardEntry { Name = "Astral Cornucopia", CardType = "Artifact", Category = "Ramp" },
            ]
        };

        var query = RagPipelineService.BuildRecommendationQuery(deck);

        Assert.Contains("Doubling Season", query);
        Assert.Contains("Astral Cornucopia", query);
        Assert.DoesNotContain("Forest", query);
        // Commander card name still appears from the Commander field, but not as a deck card
        // (the Commander field token appears before we iterate cards)
    }

    [Fact]
    public void BuildRecommendationQuery_CapsNonLandCardsAtTen()
    {
        var deck = new DeckConfiguration
        {
            Commander = "",
            Strategy = "",
            Cards = Enumerable.Range(1, 20).Select(i => new CardEntry
            {
                Name = $"Spell {i}",
                CardType = "Sorcery",
                Category = "Sorcery"
            }).ToList()
        };

        var query = RagPipelineService.BuildRecommendationQuery(deck);

        // Only the first 10 non-land cards should appear
        Assert.Contains("Spell 1", query);
        Assert.Contains("Spell 10", query);
        Assert.DoesNotContain("Spell 11", query);
        Assert.DoesNotContain("Spell 20", query);
    }

    [Fact]
    public void BuildRecommendationQuery_HandlesEmptyDeck()
    {
        var deck = new DeckConfiguration
        {
            Commander = "",
            Strategy = "",
            Cards = []
        };

        // Should not throw
        var query = RagPipelineService.BuildRecommendationQuery(deck);

        Assert.NotNull(query);
        Assert.Equal("", query.Trim());
    }

    [Fact]
    public void BuildRecommendationQuery_HandlesNullCommanderAndStrategy()
    {
        var deck = new DeckConfiguration
        {
            Commander = null!,
            Strategy = null!,
            Cards =
            [
                new CardEntry { Name = "Sol Ring", CardType = "Artifact", Category = "Ramp" }
            ]
        };

        var query = RagPipelineService.BuildRecommendationQuery(deck);

        Assert.Contains("Sol Ring", query);
    }

    // ─── GetCardRecommendationsAsync (Qdrant-grounded path) ───────────────────

    [Fact]
    public async Task GetCardRecommendationsAsync_UsesCandidates_WhenCardSearchSucceeds()
    {
        // Arrange: card search returns two candidates; LLM picks one
        var candidatesJson = JsonSerializer.Serialize(new[]
        {
            new { name = "Doubling Season", typeLine = "Enchantment", manaCost = "{4}{G}", cmc = 5.0, priceUsd = 30.0, oracleText = "If an effect would put counters on..." },
            new { name = "Hardened Scales", typeLine = "Enchantment", manaCost = "{G}", cmc = 1.0, priceUsd = 4.0, oracleText = "If one or more +1/+1 counters..." }
        });

        var llmJson = JsonSerializer.Serialize(new[]
        {
            new { name = "Doubling Season", reason = "Synergy with proliferate.", category = "Enchantment", estimatedBudgetTier = "expensive" }
        });

        var llmResponse = WrapInChatResponse(llmJson);

        var service = CreateService(cardSearchJson: candidatesJson, llmResponseJson: llmResponse);
        var deck = MakeDeck();

        // Act
        var recs = await service.GetCardRecommendationsAsync(deck);

        // Assert
        Assert.Single(recs);
        Assert.Equal("Doubling Season", recs[0].Name);
        Assert.Equal("Enchantment", recs[0].Category);
        Assert.Equal("expensive", recs[0].EstimatedBudgetTier);
    }

    [Fact]
    public async Task GetCardRecommendationsAsync_FallsBackToPureLlm_WhenCardSearchFails()
    {
        // Arrange: card search returns 500; LLM path returns a recommendation anyway
        var llmJson = JsonSerializer.Serialize(new[]
        {
            new { name = "Sol Ring", reason = "Staple ramp.", category = "Ramp", estimatedBudgetTier = "budget" }
        });

        var llmResponse = WrapInChatResponse(llmJson);

        // card search will fail (503), LLM call returns fine
        var service = CreateService(cardSearchStatusCode: HttpStatusCode.ServiceUnavailable, llmResponseJson: llmResponse);
        var deck = MakeDeck();

        // Act
        var recs = await service.GetCardRecommendationsAsync(deck);

        // Assert: at least the fallback path returned the LLM suggestion
        Assert.Single(recs);
        Assert.Equal("Sol Ring", recs[0].Name);
    }

    [Fact]
    public async Task GetCardRecommendationsAsync_FiltersCardsAlreadyInDeck()
    {
        // Arrange: LLM returns two cards; one is already in the deck
        var candidatesJson = JsonSerializer.Serialize(new[]
        {
            new { name = "Doubling Season", typeLine = "Enchantment", manaCost = "{4}{G}", cmc = 5.0, priceUsd = 30.0, oracleText = "" },
            new { name = "Hardened Scales", typeLine = "Enchantment", manaCost = "{G}", cmc = 1.0, priceUsd = 4.0, oracleText = "" }
        });

        var llmJson = JsonSerializer.Serialize(new[]
        {
            new { name = "Doubling Season", reason = "Synergy.", category = "Enchantment", estimatedBudgetTier = "expensive" },
            new { name = "Hardened Scales", reason = "Also synergy.", category = "Enchantment", estimatedBudgetTier = "budget" }
        });
        var llmResponse = WrapInChatResponse(llmJson);

        var deck = MakeDeck();
        // Put Doubling Season already in the deck
        deck.Cards.Add(new CardEntry { Name = "Doubling Season", Category = "Enchantment" });

        var service = CreateService(cardSearchJson: candidatesJson, llmResponseJson: llmResponse);

        var recs = await service.GetCardRecommendationsAsync(deck);

        Assert.Single(recs);
        Assert.Equal("Hardened Scales", recs[0].Name);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DeckConfiguration MakeDeck() => new()
    {
        Id = "deck-test-1",
        DeckName = "Test Deck",
        Commander = "Atraxa, Praetors' Voice",
        Strategy = "Proliferate counters",
        Format = "Commander",
        Colors = ["W", "U", "B", "G"],
        PowerLevel = "Focused",
        BudgetRange = "Mid-range ($50-$150)",
        Cards =
        [
            new CardEntry { Name = "Sol Ring", CardType = "Artifact", Category = "Ramp" },
            new CardEntry { Name = "Kodama's Reach", CardType = "Sorcery", Category = "Ramp" }
        ]
    };

    private static string WrapInChatResponse(string content)
    {
        var obj = new
        {
            choices = new[]
            {
                new { message = new { content } }
            },
            usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 }
        };
        return JsonSerializer.Serialize(obj);
    }

    private static RagPipelineService CreateService(
        string? cardSearchJson = null,
        HttpStatusCode cardSearchStatusCode = HttpStatusCode.OK,
        string? llmResponseJson = null)
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            // Route based on request path
            if (req.RequestUri?.AbsolutePath.Contains("/api/cards/search") == true)
            {
                if (cardSearchStatusCode != HttpStatusCode.OK)
                    return Task.FromResult(new HttpResponseMessage(cardSearchStatusCode));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(cardSearchJson ?? "[]", Encoding.UTF8, "application/json")
                });
            }

            // LLM chat completions endpoint
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(llmResponseJson ?? WrapInChatResponse("[]"), Encoding.UTF8, "application/json")
            });
        });

        var factory = new StubHttpClientFactory(handler);
        var settings = Options.Create(new RagPipelineSettings
        {
            BaseUrl = "http://forge-ai-api:8080",
            LlmBaseUrl = "https://api.deepinfra.com/v1/openai",
            LlmApiKey = "test-key",
            Model = "meta-llama/Llama-3.3-70B-Instruct"
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
