using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Implements IDeckGenerationService using the RAG pipeline: mtg-forge-ai + Together.ai.
///
/// Deck generation calls mtg-forge-ai, which uses Qdrant vector search to pre-filter
/// cards by price and color identity before passing them to the hosted LLM — solving
/// the budget compliance and card legality problems without relying on the LLM to
/// estimate prices or enforce color restrictions.
///
/// Deck analysis and import descriptions call Together.ai directly using the
/// OpenAI-compatible /v1/chat/completions endpoint.
///
/// Works both locally (localhost endpoints) and on Railway (internal DNS endpoints).
/// </summary>
public class RagPipelineService : IDeckGenerationService
{
    private readonly IHttpClientFactory _factory;
    private readonly RagPipelineSettings _settings;
    private readonly ILogger<RagPipelineService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public RagPipelineService(
        IHttpClientFactory factory,
        IOptions<RagPipelineSettings> settings,
        ILogger<RagPipelineService> logger)
    {
        _factory = factory;
        _settings = settings.Value;
        _logger = logger;
    }

    // ─── Deck Generation ──────────────────────────────────────────────────────

    public async Task<DeckConfiguration> GenerateDeckAsync(DeckGenerationRequest request)
    {
        _logger.LogInformation(
            "RagPipelineService: generating {Format} deck via mtg-forge-ai at {Url}",
            request.Format, _settings.BaseUrl);

        var client = CreateMtgForgeClient();

        var localRequest = new
        {
            format       = request.Format.ToLowerInvariant(),
            theme        = request.PreferredStrategy ?? "balanced",
            budget       = MapBudget(request.BudgetRange),
            powerLevel   = MapPowerLevel(request.PowerLevel),
            commander    = request.PreferredCommander,
            colorIdentity = request.Colors,
            extraContext  = request.AdditionalNotes
        };

        var json = JsonSerializer.Serialize(localRequest, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/decks/generate", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("mtg-forge-ai error {Status}: {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"mtg-forge-ai returned {response.StatusCode}: {err}");
        }

        var body = await response.Content.ReadAsStringAsync();
        var localDeck = JsonSerializer.Deserialize<LocalDeckResponse>(body, JsonOpts)
            ?? throw new InvalidOperationException("Null response from mtg-forge-ai");

        return MapToDeckConfiguration(localDeck, request);
    }

    // ─── Deck Analysis ────────────────────────────────────────────────────────

    public async Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck)
    {
        _logger.LogInformation("RagPipelineService: analyzing deck '{Name}' via Together.ai", deck.DeckName);

        var cardList = string.Join("\n", deck.Cards.Select(c =>
            $"- {c.Quantity}x {c.Name} ({c.CardType}, CMC {c.Cmc}): {c.RoleInDeck}"));

        var schema = """
            {
              "synergyAssessment": "string - 2-3 sentence assessment",
              "overallRating": "string - one of: Weak, Below Average, Average, Good, Strong, Excellent",
              "weaknesses": ["string"],
              "improvementSuggestions": ["string"],
              "cardUpgrades": [
                {
                  "removeCard": "exact card name to remove",
                  "addCard": "exact card name to add",
                  "reason": "why this swap improves the deck"
                }
              ]
            }
            """;

        var prompt = $"""
            You are a Magic: The Gathering deck analysis expert. Analyze the following deck and provide detailed, actionable feedback.

            Deck: {deck.DeckName}
            Format: {deck.Format}
            Colors: {string.Join(", ", deck.Colors)}
            Commander: {(string.IsNullOrEmpty(deck.Commander) ? "N/A" : deck.Commander)}
            Strategy: {deck.Strategy}
            Power Level: {deck.PowerLevel}

            Card List:
            {cardList}

            Respond with ONLY a valid JSON object (no markdown, no explanation) matching this exact schema:
            {schema}

            Provide 3-5 weaknesses, 3-5 improvement suggestions, and 3-5 card upgrade recommendations.
            """;

        var rawResponse = await CallLlmAsync(prompt);

        var jsonContent = ExtractJson(rawResponse);
        var analysis = JsonSerializer.Deserialize<DeckAnalysis>(jsonContent, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize analysis from Together.ai");

        return analysis;
    }

    // ─── Budget Replacements ──────────────────────────────────────────────────

    /// <summary>
    /// Budget is enforced upstream by Qdrant price filtering in mtg-forge-ai,
    /// so replacements are rarely needed. Returns empty to skip the retry loop.
    /// </summary>
    public Task<List<CardEntry>> SuggestBudgetReplacementsAsync(
        DeckConfiguration deck,
        List<CardEntry> expensiveCards,
        decimal currentTotal,
        decimal budgetMax,
        List<(string CardName, decimal Price)> cheapCardPool)
    {
        _logger.LogInformation(
            "RagPipelineService: budget enforcement skipped (cards are pre-filtered by Qdrant price)");
        return Task.FromResult<List<CardEntry>>([]);
    }

    // ─── Import Description ───────────────────────────────────────────────────

    public async Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards)
    {
        var sample = cards
            .Where(c => c.CardType == null || !c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => $"- {c.Quantity}x {c.Name} ({c.CardType ?? "Unknown"})");

        var prompt = $"""
            You are a Magic: The Gathering expert. Based on the following deck card list, write a concise 2-3 sentence
            flavorful description of the deck's playstyle and theme. Respond with ONLY the description text, no JSON.

            Deck Name: {deckName}
            Sample Cards:
            {string.Join("\n", sample)}
            """;

        try
        {
            return (await CallLlmAsync(prompt)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate import description via Together.ai");
            return $"Imported deck: {deckName}";
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient CreateMtgForgeClient()
    {
        var client = _factory.CreateClient();
        client.BaseAddress = new Uri(_settings.BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    private async Task<string> CallLlmAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_settings.LlmApiKey))
            throw new InvalidOperationException(
                "RagPipeline:LlmApiKey is required. Set it via environment variable RAGPIPELINE__LLMAPIKEY.");

        var client = _factory.CreateClient();
        client.BaseAddress = new Uri(_settings.LlmBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.LlmApiKey);

        var payload = new
        {
            model = _settings.Model,
            stream = false,
            max_tokens = 4096,
            temperature = 0.7,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/chat/completions", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Together.ai error {response.StatusCode}: {err}");
        }

        var body = await response.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Empty response from Together.ai");
    }

    private static DeckConfiguration MapToDeckConfiguration(
        LocalDeckResponse local,
        DeckGenerationRequest request)
    {
        var cards = local.Sections
            .SelectMany(s => s.Cards.Select(c => new CardEntry
            {
                Name           = c.Name,
                Quantity       = c.Quantity,
                ManaCost       = c.ManaCost ?? "",
                Cmc            = (int)Math.Round(c.Cmc),
                CardType       = c.TypeLine ?? "",
                Category       = s.Category,
                RoleInDeck     = c.OracleText ?? "",
                EstimatedPrice = (decimal)c.PriceUsd
            }))
            .ToList();

        var deckName = !string.IsNullOrWhiteSpace(local.Commander)
            ? $"{local.Commander} — {local.Theme}"
            : $"{local.Theme} ({request.Format})";

        return new DeckConfiguration
        {
            DeckName           = deckName,
            Commander          = local.Commander ?? "",
            Strategy           = local.Reasoning,
            DeckDescription    = local.Reasoning,
            Format             = request.Format,
            Colors             = request.Colors,
            PowerLevel         = request.PowerLevel,
            BudgetRange        = request.BudgetRange,
            Cards              = cards,
            TotalCards         = cards.Sum(c => c.Quantity),
            EstimatedTotalPrice = (decimal)local.EstimatedCost,
        };
    }

    private static double MapBudget(string budgetRange) =>
        ClaudeService.GetBudgetMax(budgetRange) is { } max ? (double)max : 10_000.0;

    private static int MapPowerLevel(string powerLevel) => powerLevel.ToLower() switch
    {
        "casual"      => 4,
        "focused"     => 6,
        "optimized"   => 8,
        "competitive" => 10,
        _             => 5
    };

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }

    // ─── mtg-forge-ai Response DTOs ───────────────────────────────────────

    private class LocalDeckResponse
    {
        public string? Commander { get; set; }
        public string Theme { get; set; } = "";
        public string Format { get; set; } = "";
        public List<LocalSection> Sections { get; set; } = [];
        public double EstimatedCost { get; set; }
        public string Reasoning { get; set; } = "";
    }

    private class LocalSection
    {
        public string Category { get; set; } = "";
        public List<LocalCard> Cards { get; set; } = [];
    }

    private class LocalCard
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public double PriceUsd { get; set; }
        public string? OracleText { get; set; }
        public string? ManaCost { get; set; }
        public double Cmc { get; set; }
        public string? TypeLine { get; set; }
    }

    // ─── Together.ai / OpenAI-compatible Response DTOs ───────────────────────

    private class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }

    private class ChatMessage
    {
        public string Content { get; set; } = "";
    }
}
