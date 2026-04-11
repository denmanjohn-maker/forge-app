using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Tests;

public class ClaudeServiceTests
{
    [Fact]
    public async Task GenerateDeckAsync_ParsesJsonFromMarkdownCodeBlock()
    {
        var responseJson = """
        {
          "content": [
            {
              "type": "text",
              "text": "```json\n{\"deckName\":\"Test Deck\",\"commander\":\"Atraxa, Praetors' Voice\",\"strategy\":\"Proliferate value\",\"estimatedTotalPrice\":123.45,\"totalCards\":100,\"deckDescription\":\"desc\",\"cards\":[{\"name\":\"Sol Ring\",\"quantity\":1,\"manaCost\":\"{1}\",\"cmc\":1,\"cardType\":\"Artifact\",\"category\":\"Artifact\",\"roleInDeck\":\"Ramp\",\"estimatedPrice\":1.25}]}\n```"
            }
          ]
        }
        """;

        var service = CreateClaudeService(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            })));

        var request = new DeckGenerationRequest
        {
            Colors = ["U", "B"],
            Format = "Commander",
            PowerLevel = "High",
            BudgetRange = "Mid"
        };

        var result = await service.GenerateDeckAsync(request);

        Assert.Equal("Test Deck", result.DeckName);
        Assert.Equal(["U", "B"], result.Colors);
        Assert.Equal("Commander", result.Format);
        Assert.Equal("High", result.PowerLevel);
        Assert.Equal("Mid", result.BudgetRange);
        Assert.Single(result.Cards);
    }

    [Fact]
    public async Task GenerateDeckAsync_Throws_WhenApiFails()
    {
        var service = CreateClaudeService(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request")
            })));

        var ex = await Assert.ThrowsAsync<Exception>(() => service.GenerateDeckAsync(new DeckGenerationRequest { Colors = ["G"] }));
        Assert.Contains("Claude API returned", ex.Message);
    }

    [Fact]
    public async Task AnalyzeDeckAsync_ParsesRawJsonText()
    {
        var responseJson = """
        {
          "content": [
            {
              "type": "text",
              "text": "{\"synergyAssessment\":\"Good synergy\",\"overallRating\":\"Good\",\"weaknesses\":[\"Few wipes\"],\"improvementSuggestions\":[\"More draw\"],\"cardUpgrades\":[{\"removeCard\":\"Cancel\",\"addCard\":\"Swan Song\",\"reason\":\"Efficiency\"}]}"
            }
          ]
        }
        """;

        var service = CreateClaudeService(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            })));

        var analysis = await service.AnalyzeDeckAsync(new DeckConfiguration
        {
            DeckName = "X",
            Format = "Commander",
            Colors = ["U"],
            Strategy = "Control",
            PowerLevel = "Mid",
            BudgetRange = "Budget",
            TotalCards = 100,
            Cards = [new CardEntry { Name = "Island", Quantity = 1, CardType = "Land", RoleInDeck = "Land", Category = "Land" }]
        });

        Assert.Equal("Good", analysis.OverallRating);
        Assert.Single(analysis.Weaknesses);
        Assert.Single(analysis.CardUpgrades);
    }

    [Fact]
    public async Task GenerateImportDescriptionAsync_ReturnsFallback_WhenApiThrows()
    {
        var service = CreateClaudeService(new StubHttpMessageHandler(_ => throw new HttpRequestException("network")));

        var result = await service.GenerateImportDescriptionAsync("My Deck", []);

        Assert.Equal("Imported deck: My Deck", result);
    }

    private static ClaudeService CreateClaudeService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var settings = Options.Create(new ClaudeApiSettings
        {
            ApiKey = "test",
            Model = "test-model",
            MaxTokens = 1000
        });
        return new ClaudeService(httpClient, settings, NullLogger<ClaudeService>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handlerFunc(request);
    }
}
