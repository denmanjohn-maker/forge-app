using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Tests;

public class ScryfallServiceTests
{
    [Fact]
    public async Task EnrichCardsAsync_EnrichesMissingFields_AndPreservesExistingValues()
    {
        var responseJson = """
        {
          "data": [
            {
              "name": "Sol Ring",
              "mana_cost": "{1}",
              "cmc": 1,
              "type_line": "Artifact",
              "prices": { "usd": "1.50" },
              "color_identity": []
            },
            {
              "name": "Lightning Bolt",
              "mana_cost": "{R}",
              "cmc": 1,
              "type_line": "Instant",
              "prices": { "usd": "2.25" },
              "color_identity": ["R"]
            }
          ]
        }
        """;

        var service = new ScryfallService(
            new HttpClient(new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                }))),
            NullLogger<ScryfallService>.Instance);

        var cards = new List<CardEntry>
        {
            new() { Name = "Sol Ring", Quantity = 1, Category = "Artifact" },
            new() { Name = "Lightning Bolt", Quantity = 1, Category = "Instant", ManaCost = "{R}", Cmc = 9, CardType = "Custom", EstimatedPrice = 9.99m }
        };

        var enriched = await service.EnrichCardsAsync(cards);

        Assert.Equal("{1}", enriched[0].ManaCost);
        Assert.Equal(1, enriched[0].Cmc);
        Assert.Equal("Artifact", enriched[0].CardType);
        Assert.Equal(1.50m, enriched[0].EstimatedPrice);

        Assert.Equal("{R}", enriched[1].ManaCost);
        Assert.Equal(9, enriched[1].Cmc);
        Assert.Equal("Custom", enriched[1].CardType);
        Assert.Equal(9.99m, enriched[1].EstimatedPrice);
    }

    [Fact]
    public async Task EnrichCardsAsync_HandlesFailedResponse_WithoutThrowing()
    {
        var service = new ScryfallService(
            new HttpClient(new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)))),
            NullLogger<ScryfallService>.Instance);

        var cards = new List<CardEntry> { new() { Name = "Card A", Quantity = 1, Category = "Mainboard" } };
        var result = await service.EnrichCardsAsync(cards);

        Assert.Same(cards, result);
        Assert.Equal("Card A", result[0].Name);
    }

    [Fact]
    public void DeriveColors_ReturnsUniqueColorsInWubrgOrder()
    {
        var service = new ScryfallService(new HttpClient(new StubHttpMessageHandler(_ => throw new NotImplementedException())), NullLogger<ScryfallService>.Instance);

        var colors = service.DeriveColors([
            new CardEntry { Name = "A", ManaCost = "{R}{G}", Category = "Mainboard" },
            new CardEntry { Name = "B", ManaCost = "{U}{B}", Category = "Mainboard" },
            new CardEntry { Name = "C", ManaCost = "{W}", Category = "Mainboard" },
            new CardEntry { Name = "D", ManaCost = "{U}", Category = "Mainboard" }
        ]);

        Assert.Equal(["W", "U", "B", "R", "G"], colors);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handlerFunc(request);
    }
}
