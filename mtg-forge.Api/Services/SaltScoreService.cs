using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MtgForge.Api.Services;

/// <summary>
/// Fetches and caches card "salt scores" from the EDHREC public API.
/// Salt scores represent how much other players dislike playing against a given card
/// in Commander, based on community survey data.
/// <para>
/// Results are cached in memory for 24 hours to avoid hammering the EDHREC endpoint.
/// Returns an empty dictionary on fetch or parse failure rather than propagating errors.
/// </para>
/// </summary>
public class SaltScoreService
{
    private readonly IHttpClientFactory _factory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SaltScoreService> _logger;

    private const string CacheKey = "edhrec_salt_scores";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public SaltScoreService(IHttpClientFactory factory, IMemoryCache cache, ILogger<SaltScoreService> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns a dictionary mapping card name → salt score, sourced from EDHREC.
    /// Results are served from an in-memory cache (24-hour TTL) after the first fetch.
    /// Returns an empty dictionary when the endpoint is unreachable or returns an error.
    /// </summary>
    public async Task<Dictionary<string, double>> GetSaltScoresAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, double>? cached) && cached != null)
            return cached;

        try
        {
            var client = _factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("mtg-forge/1.0");

            var response = await client.GetAsync("https://json.edhrec.com/pages/top/salt.json");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EDHREC salt endpoint returned {Status}", response.StatusCode);
                return new Dictionary<string, double>();
            }

            var body = await response.Content.ReadAsStringAsync();
            var scores = ParseSaltScores(body);

            _cache.Set(CacheKey, scores, CacheDuration);
            _logger.LogInformation("Loaded {Count} salt scores from EDHREC", scores.Count);
            return scores;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch salt scores from EDHREC");
            return new Dictionary<string, double>();
        }
    }

    private static Dictionary<string, double> ParseSaltScores(string json)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            // EDHREC salt JSON has container.cardlists[0].cardviews or similar; try multiple shapes
            if (doc.RootElement.TryGetProperty("container", out var container) &&
                container.TryGetProperty("cardlists", out var cardlists) &&
                cardlists.GetArrayLength() > 0)
            {
                var first = cardlists[0];
                if (first.TryGetProperty("cardviews", out var cardviews))
                {
                    foreach (var card in cardviews.EnumerateArray())
                        TryAddCard(card, scores);
                    return scores;
                }
            }

            // Fallback: look for a flat "cards" array anywhere in root
            if (doc.RootElement.TryGetProperty("cards", out var cardsArr))
            {
                foreach (var card in cardsArr.EnumerateArray())
                    TryAddCard(card, scores);
            }
        }
        catch (JsonException)
        {
            // Return empty on malformed JSON
        }
        return scores;
    }

    private static void TryAddCard(JsonElement card, Dictionary<string, double> scores)
    {
        string? name = null;
        double salt = 0;

        if (card.TryGetProperty("name", out var n)) name = n.GetString();
        if (card.TryGetProperty("salt", out var s))
            salt = s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0;

        if (!string.IsNullOrEmpty(name))
            scores[name] = salt;
    }
}
