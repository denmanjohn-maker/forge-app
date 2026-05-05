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

        var themedMatches = ThemedSetDetector.Detect(request.AdditionalNotes);
        var themedAddendum = ThemedSetDetector.BuildPromptAddendum(themedMatches);
        var extraContext = themedAddendum is null
            ? request.AdditionalNotes
            : string.IsNullOrWhiteSpace(request.AdditionalNotes)
                ? themedAddendum
                : $"{request.AdditionalNotes}\n\n{themedAddendum}";

        if (themedMatches.Count > 0)
        {
            var matchedNames = string.Join(", ", themedMatches.Select(s => s.DisplayName));
            _logger.LogInformation("RagPipelineService: detected themed set reference(s) in notes — {Matches}", matchedNames);
        }

        var localRequest = new
        {
            format       = request.Format.ToLowerInvariant(),
            theme        = request.PreferredStrategy ?? "balanced",
            budget       = MapBudget(request.BudgetRange),
            powerLevel   = MapPowerLevel(request.PowerLevel),
            commander    = request.PreferredCommander,
            colorIdentity = request.Colors,
            extraContext
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

        var deck = MapToDeckConfiguration(localDeck, request);
        EnforceCardCount(deck, request, _logger);
        return deck;
    }

    // ─── Deck Analysis ────────────────────────────────────────────────────────

    public async Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck)
    {
        _logger.LogInformation("RagPipelineService: analyzing deck '{Name}' via Together.ai", deck.DeckName);

        var metrics  = DeckMetricsCalculator.Calculate(deck.Cards);
        var cardList = string.Join("\n", deck.Cards.Select(c =>
            $"- {c.Quantity}x {c.Name} ({c.CardType}, CMC {c.Cmc}, ${c.EstimatedPrice:F2}): {c.RoleInDeck}"));

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

        var systemPrompt = """
            You are an expert Magic: The Gathering deck analyst. Evaluate decks on synergy, mana curve,
            category coverage (ramp, removal, card draw), budget efficiency, and format power level.
            Always respond with a valid JSON object only — no markdown, no explanation outside JSON.
            """;

        var userPrompt = $"""
            Analyze the following deck and provide detailed, actionable feedback.

            Deck: {deck.DeckName}
            Format: {deck.Format}
            Colors: {string.Join(", ", deck.Colors)}
            Commander: {(string.IsNullOrEmpty(deck.Commander) ? "N/A" : deck.Commander)}
            Strategy: {deck.Strategy}
            Power Level: {deck.PowerLevel}
            Total Cost: ${metrics.TotalCost:F2} | Budget Range: {deck.BudgetRange}

            ── Mana Curve (non-land spells) ──────────────────────────────────────
            {DeckMetricsCalculator.FormatManaCurve(metrics.ManaCurve)}
            Average CMC: {metrics.AverageCmc:F2}

            ── Category Coverage ─────────────────────────────────────────────────
            Lands: {metrics.LandCount} (recommended 36-38 for Commander)
            Creatures: {metrics.CreatureCount}
            Ramp: {(metrics.RampCount.HasValue ? metrics.RampCount.ToString() : "N/A — imported deck")} (recommended ≥10)
            Removal: {(metrics.RemovalCount.HasValue ? metrics.RemovalCount.ToString() : "N/A — imported deck")} (recommended ≥8)
            Card Draw: {(metrics.CardDrawCount.HasValue ? metrics.CardDrawCount.ToString() : "N/A — imported deck")} (recommended ≥10)

            ── Color Pip Distribution ────────────────────────────────────────────
            {DeckMetricsCalculator.FormatPips(metrics.ColorPipDistribution)}

            Card List (Qty x Name, Type, CMC, Price):
            {cardList}

            Respond with ONLY a valid JSON object matching this exact schema:
            {schema}

            Provide 3-5 weaknesses, 3-5 improvement suggestions, and 3-5 card upgrade recommendations.
            When suggesting card upgrades, consider the deck's budget — prefer swaps that stay within ${metrics.TotalCost:F2} total.
            """;

        var rawResponse = await CallLlmAsync(systemPrompt, userPrompt, jsonMode: true, temperature: 0.3);

        var jsonContent = ExtractJson(rawResponse);
        var analysis = JsonSerializer.Deserialize<DeckAnalysis>(jsonContent, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize analysis from Together.ai");

        return analysis;
    }

    // ─── Budget Replacements ──────────────────────────────────────────────────

    public async Task<List<CardEntry>> SuggestBudgetReplacementsAsync(
        DeckConfiguration deck,
        List<CardEntry> expensiveCards,
        decimal currentTotal,
        decimal budgetMax,
        List<(string CardName, decimal Price)> cheapCardPool)
    {
        var cardListStr = string.Join("\n", expensiveCards
            .Select(c => $"- {c.Name} (${c.EstimatedPrice:F2}, {c.CardType}, {c.Category}, Role: {c.RoleInDeck})"));

        var existingNames = new HashSet<string>(
            deck.Cards.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var poolStr = string.Join("\n", cheapCardPool
            .Where(c => !existingNames.Contains(c.CardName))
            .Select(c => $"- {c.CardName} (${c.Price:F2})"));

        var deckContext = $$"""
            Deck: {{deck.DeckName}}
            Format: {{deck.Format}}
            Colors: {{string.Join(", ", deck.Colors)}}
            Commander: {{deck.Commander}}
            Strategy: {{deck.Strategy}}
            Current total price: ${{currentTotal:F2}}
            Budget limit: ${{budgetMax:F2}}
            Amount over budget: ${{(currentTotal - budgetMax):F2}}
            """;

        var prompt = $$"""
            You are a Magic: The Gathering deck building expert. A generated deck is over budget.
            I need you to suggest cheaper replacement cards for the most expensive cards listed below.

            {{deckContext}}

            Expensive cards to replace:
            {{cardListStr}}

            IMPORTANT: You MUST choose replacements from this list of verified budget cards with confirmed real prices:
            {{poolStr}}

            For EACH expensive card above, pick a replacement from the budget pool that:
            1. Fills a similar role in the deck (same category/function when possible)
            2. Is legal in {{deck.Format}} format
            3. Works with the deck's color identity: {{string.Join(", ", deck.Colors)}}

            Respond with ONLY a valid JSON array (no markdown, no explanation) of replacement cards:
            [
              {
                "name": "string - exact card name from the budget pool above",
                "quantity": 1,
                "manaCost": "string - mana cost like {2}{B}{G}",
                "cmc": 0,
                "cardType": "string - e.g. Creature - Elf Shaman",
                "category": "string - must match the category of the card it replaces",
                "roleInDeck": "string - brief explanation",
                "estimatedPrice": 0.0
              }
            ]

            Return exactly {{expensiveCards.Count}} replacement cards, one for each expensive card, in the same order.
            You MUST only use card names from the budget pool list above — do not suggest cards outside that list.
            """;

        var systemPrompt = """
            You are an expert Magic: The Gathering deckbuilder specializing in budget optimization.
            Always respond with a valid JSON array only — no markdown, no explanation outside JSON.
            """;

        try
        {
            var rawResponse = await CallLlmAsync(systemPrompt, prompt, jsonMode: true, temperature: 0.3);
            var jsonContent = ExtractJson(rawResponse);
            return JsonSerializer.Deserialize<List<CardEntry>>(jsonContent, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RagPipelineService: failed to parse budget replacement suggestions from Together.ai");
            return [];
        }
    }

    // ─── Import Description ───────────────────────────────────────────────────

    public async Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards)
    {
        var sample = cards
            .Where(c => c.CardType == null || !c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => $"- {c.Quantity}x {c.Name} ({c.CardType ?? "Unknown"})");

        var systemPrompt = "You are a Magic: The Gathering expert. Write vivid, concise deck descriptions. Respond with plain text only — no JSON, no markdown.";

        var userPrompt = $"""
            Based on the following card list, write a concise 2-3 sentence flavorful description
            of the deck's playstyle and theme.

            Deck Name: {deckName}
            Sample Cards:
            {string.Join("\n", sample)}
            """;

        try
        {
            return (await CallLlmAsync(systemPrompt, userPrompt, jsonMode: false, temperature: 0.7)).Trim();
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

    private async Task<string> CallLlmAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode = false,
        double temperature = 0.7)
    {
        if (string.IsNullOrWhiteSpace(_settings.LlmApiKey))
            throw new InvalidOperationException(
                "RagPipeline:LlmApiKey is required. Set it via environment variable RAGPIPELINE__LLMAPIKEY.");

        var client = _factory.CreateClient();
        client.BaseAddress = new Uri(_settings.LlmBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.LlmApiKey);

        object? responseFormat = jsonMode ? new { type = "json_object" } : null;

        var payload = new
        {
            model = _settings.Model,
            stream = false,
            max_tokens = 4096,
            temperature,
            response_format = responseFormat,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            }
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
        BudgetHelper.GetBudgetMax(budgetRange) is { } max ? (double)max : 10_000.0;

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
        // Try to find JSON in code blocks first
        var codeBlockStart = text.IndexOf("```json");
        if (codeBlockStart >= 0)
        {
            var jsonStart = text.IndexOf('\n', codeBlockStart) + 1;
            var jsonEnd = text.IndexOf("```", jsonStart);
            if (jsonEnd > jsonStart)
                return text[jsonStart..jsonEnd].Trim();
        }

        codeBlockStart = text.IndexOf("```");
        if (codeBlockStart >= 0)
        {
            var jsonStart = text.IndexOf('\n', codeBlockStart) + 1;
            var jsonEnd = text.IndexOf("```", jsonStart);
            if (jsonEnd > jsonStart)
                return text[jsonStart..jsonEnd].Trim();
        }

        // Try to find raw JSON object or array
        var braceStart  = text.IndexOf('{');
        var braceEnd    = text.LastIndexOf('}');
        var bracketStart = text.IndexOf('[');
        var bracketEnd  = text.LastIndexOf(']');

        // Pick whichever delimiter comes first (array or object)
        if (bracketStart >= 0 && bracketEnd > bracketStart
            && (braceStart < 0 || bracketStart < braceStart))
            return text[bracketStart..(bracketEnd + 1)];

        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return text.Trim();
    }

    private const decimal BasicLandPrice = 0.25m;

    private static void EnforceCardCount(
        DeckConfiguration deck,
        DeckGenerationRequest request,
        ILogger logger)
    {
        if (!request.Format.Equals("Commander", StringComparison.OrdinalIgnoreCase))
        {
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
            return;
        }

        var totalCards = deck.Cards.Sum(c => c.Quantity);
        if (totalCards == 100)
        {
            deck.TotalCards = 100;
            return;
        }

        logger.LogWarning(
            "RagPipelineService: Commander deck has {ActualCount} cards instead of 100. Adjusting.",
            totalCards);

        // Commander is singleton — force all quantities to 1
        foreach (var card in deck.Cards)
            card.Quantity = 1;

        totalCards = deck.Cards.Count;

        if (totalCards > 100)
        {
            var excessCount = totalCards - 100;
            var removable = deck.Cards
                .Where(c => !c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase)
                         && !c.Category.Equals("Land", StringComparison.OrdinalIgnoreCase))
                .TakeLast(excessCount)
                .ToList();
            foreach (var card in removable)
                deck.Cards.Remove(card);
        }
        else if (totalCards < 100)
        {
            var deficit = 100 - totalCards;
            var basicLands = GetBasicLandsForColors(request.Colors);

            for (var landIndex = 0; landIndex < deficit; landIndex++)
            {
                deck.Cards.Add(new CardEntry
                {
                    Name           = basicLands[landIndex % basicLands.Count],
                    Quantity       = 1,
                    ManaCost       = "",
                    Cmc            = 0,
                    CardType       = "Basic Land",
                    Category       = "Land",
                    RoleInDeck     = "Mana base (auto-added to reach 100 cards)",
                    EstimatedPrice = BasicLandPrice
                });
            }

            logger.LogWarning(
                "RagPipelineService: padded Commander deck with {Deficit} basic lands to reach 100 cards.",
                deficit);
        }

        deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
    }

    private static List<string> GetBasicLandsForColors(List<string> colors)
    {
        var lands = new List<string>();
        foreach (var color in colors)
        {
            var land = color.ToUpperInvariant() switch
            {
                "W" => "Plains",
                "U" => "Island",
                "B" => "Swamp",
                "R" => "Mountain",
                "G" => "Forest",
                _   => null
            };
            if (land != null)
                lands.Add(land);
        }

        if (lands.Count == 0)
            lands.Add("Wastes");

        return lands;
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
