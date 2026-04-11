using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public class ScryfallService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ScryfallService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ScryfallService(HttpClient httpClient, ILogger<ScryfallService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("mtg-forge/1.0");
    }

    /// <summary>
    /// Enrich a list of CardEntry objects with data from Scryfall (mana cost, CMC, type, price, color identity).
    /// Cards that are not found are left unchanged.
    /// </summary>
    public async Task<List<CardEntry>> EnrichCardsAsync(List<CardEntry> cards)
    {
        if (cards.Count == 0) return cards;

        // Batch into groups of 75 (Scryfall limit)
        const int batchSize = 75;
        var scrayfallData = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);

        var distinctNames = cards.Select(c => c.Name).Distinct().ToList();

        for (int i = 0; i < distinctNames.Count; i += batchSize)
        {
            var batch = distinctNames.Skip(i).Take(batchSize).ToList();
            var identifiers = batch.Select(name => new { name }).ToList();
            var body = JsonSerializer.Serialize(new { identifiers });

            try
            {
                var response = await _httpClient.PostAsync(
                    "https://api.scryfall.com/cards/collection",
                    new StringContent(body, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Scryfall collection returned {Status}", response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ScryfallCollectionResult>(json, _jsonOpts);

                if (result?.Data != null)
                {
                    foreach (var card in result.Data)
                    {
                        if (!string.IsNullOrEmpty(card.Name))
                        {
                            scrayfallData[card.Name] = card;
                            // Double-faced cards return "Card A // Card B" — also index by front face
                            var slashIdx = card.Name.IndexOf(" // ", StringComparison.Ordinal);
                            if (slashIdx > 0)
                                scrayfallData[card.Name[..slashIdx]] = card;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scryfall batch enrichment failed for batch starting at {Index}", i);
            }

            // Respect Scryfall rate limit (10 requests/sec)
            if (i + batchSize < distinctNames.Count)
                await Task.Delay(110);
        }

        // Enrich each card
        foreach (var card in cards)
        {
            if (!scrayfallData.TryGetValue(card.Name, out var sf)) continue;

            if (string.IsNullOrEmpty(card.ManaCost) && !string.IsNullOrEmpty(sf.ManaCost))
                card.ManaCost = sf.ManaCost;

            if (card.Cmc == 0 && sf.Cmc > 0)
                card.Cmc = (int)sf.Cmc;

            if (string.IsNullOrEmpty(card.CardType) && !string.IsNullOrEmpty(sf.TypeLine))
                card.CardType = sf.TypeLine;

            if (card.EstimatedPrice == 0 && sf.Prices?.Usd != null
                && decimal.TryParse(sf.Prices.Usd, out var price))
                card.EstimatedPrice = price;
        }

        return cards;
    }

    /// <summary>
    /// Derives color identity from enriched card entries, falling back to scryfall data.
    /// Returns WUBRG ordering.
    /// </summary>
    public List<string> DeriveColors(List<CardEntry> cards)
    {
        var colorSet = new HashSet<string>();
        foreach (var card in cards)
        {
            if (!string.IsNullOrEmpty(card.ManaCost))
            {
                if (card.ManaCost.Contains("W")) colorSet.Add("W");
                if (card.ManaCost.Contains("U")) colorSet.Add("U");
                if (card.ManaCost.Contains("B")) colorSet.Add("B");
                if (card.ManaCost.Contains("R")) colorSet.Add("R");
                if (card.ManaCost.Contains("G")) colorSet.Add("G");
            }
        }

        // Return in WUBRG order
        var order = new[] { "W", "U", "B", "R", "G" };
        return order.Where(colorSet.Contains).ToList();
    }
}

// Scryfall response models
public class ScryfallCollectionResult
{
    [JsonPropertyName("data")]
    public List<ScryfallCard>? Data { get; set; }
}

public class ScryfallCard
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mana_cost")]
    public string? ManaCost { get; set; }

    [JsonPropertyName("cmc")]
    public float Cmc { get; set; }

    [JsonPropertyName("type_line")]
    public string? TypeLine { get; set; }

    [JsonPropertyName("prices")]
    public ScryfallPrices? Prices { get; set; }

    [JsonPropertyName("color_identity")]
    public List<string>? ColorIdentity { get; set; }
}

public class ScryfallPrices
{
    [JsonPropertyName("usd")]
    public string? Usd { get; set; }
}
