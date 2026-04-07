using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

/// <summary>
/// Implements IDeckGenerationService using a hosted Ollama instance directly.
///
/// Uses the same prompt structure and JSON parsing as ClaudeService but targets
/// the Ollama /api/chat endpoint. Designed for Railway deployment where Ollama
/// runs as a separate internal service (e.g. ollama.railway.internal).
/// </summary>
public class OllamaService : IDeckGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    private readonly ILogger<OllamaService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions OllamaJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaService(
        HttpClient httpClient,
        IOptions<OllamaSettings> settings,
        ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    // ─── Deck Generation ──────────────────────────────────────────────────────

    public async Task<DeckConfiguration> GenerateDeckAsync(DeckGenerationRequest request)
    {
        _logger.LogInformation(
            "OllamaService: generating {Format} deck via Ollama at {Url} (model={Model})",
            request.Format, _settings.BaseUrl, _settings.Model);

        var colorNames = request.Colors.Select(c => c switch
        {
            "W" => "White",
            "U" => "Blue",
            "B" => "Black",
            "R" => "Red",
            "G" => "Green",
            _ => c
        }).ToList();

        var colorIdentity = string.Join("/", colorNames);
        var prompt = BuildDeckPrompt(request, colorIdentity);

        var rawResponse = await CallOllamaAsync(prompt);
        var jsonContent = ExtractJson(rawResponse);

        DeckConfiguration? deck;
        try
        {
            deck = JsonSerializer.Deserialize<DeckConfiguration>(jsonContent, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Ollama deck JSON, attempting repair");
            jsonContent = RepairTruncatedDeckJson(jsonContent);
            deck = JsonSerializer.Deserialize<DeckConfiguration>(jsonContent, JsonOpts);
        }

        if (deck == null)
            throw new InvalidOperationException("Failed to deserialize deck configuration from Ollama response");

        deck.Colors = request.Colors;
        deck.Format = request.Format;
        deck.PowerLevel = request.PowerLevel;
        deck.BudgetRange = request.BudgetRange;

        if (request.Format.Equals("Commander", StringComparison.OrdinalIgnoreCase))
            EnforceCommanderCardCount(deck, request);

        deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
        return deck;
    }

    // ─── Deck Analysis ────────────────────────────────────────────────────────

    public async Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck)
    {
        _logger.LogInformation("OllamaService: analyzing deck '{Name}'", deck.DeckName);

        var cardList = string.Join("\n", deck.Cards.Select(c =>
            $"- {c.Quantity}x {c.Name} ({c.CardType}, CMC {c.Cmc}): {c.RoleInDeck}"));

        var prompt = @$"You are a Magic: The Gathering deck analysis expert. Analyze the following deck and provide detailed, actionable feedback.

Deck: {deck.DeckName}
Format: {deck.Format}
Colors: {string.Join(", ", deck.Colors)}
Commander: {(string.IsNullOrEmpty(deck.Commander) ? "N/A" : deck.Commander)}
Strategy: {deck.Strategy}
Power Level: {deck.PowerLevel}
Budget: {deck.BudgetRange}
Total Cards: {deck.TotalCards}

Card List:
{cardList}

Respond with ONLY a valid JSON object (no markdown, no explanation) matching this exact schema:
{{
  ""synergyAssessment"": ""string - 2-3 sentence assessment of how well the cards synergize together"",
  ""overallRating"": ""string - one of: Weak, Below Average, Average, Good, Strong, Excellent"",
  ""weaknesses"": [""string"", ""string""],
  ""improvementSuggestions"": [""string"", ""string""],
  ""cardUpgrades"": [
    {{
      ""removeCard"": ""string - exact card name to remove"",
      ""addCard"": ""string - exact card name to add instead"",
      ""reason"": ""string - why this swap improves the deck""
    }}
  ]
}}

Provide 3-5 weaknesses, 3-5 improvement suggestions, and 3-5 card upgrade recommendations. Use real Magic: The Gathering card names for upgrades.";

        var rawResponse = await CallOllamaAsync(prompt);
        var jsonContent = ExtractJson(rawResponse);

        var analysis = JsonSerializer.Deserialize<DeckAnalysis>(jsonContent, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize analysis from Ollama");

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

        var prompt = @$"You are a Magic: The Gathering deck building expert. A generated deck is over budget.
I need you to suggest cheaper replacement cards for the most expensive cards listed below.

Deck: {deck.DeckName}
Format: {deck.Format}
Colors: {string.Join(", ", deck.Colors)}
Commander: {deck.Commander}
Strategy: {deck.Strategy}
Current total price: ${currentTotal:F2}
Budget limit: ${budgetMax:F2}
Amount over budget: ${(currentTotal - budgetMax):F2}

Expensive cards to replace:
{cardListStr}

IMPORTANT: You MUST choose replacements from this list of verified budget cards with confirmed real prices:
{poolStr}

For EACH expensive card above, pick a replacement from the budget pool that:
1. Fills a similar role in the deck (same category/function when possible)
2. Is legal in {deck.Format} format
3. Works with the deck's color identity: {string.Join(", ", deck.Colors)}

Respond with ONLY a valid JSON array (no markdown, no explanation) of replacement cards:
[
  {{
    ""name"": ""string - exact card name from the budget pool above"",
    ""quantity"": 1,
    ""manaCost"": ""string - mana cost like {{2}}{{B}}{{G}}"",
    ""cmc"": number,
    ""cardType"": ""string - e.g. Creature - Elf Shaman"",
    ""category"": ""string - must match the category of the card it replaces"",
    ""roleInDeck"": ""string - brief explanation"",
    ""estimatedPrice"": number
  }}
]

Return exactly {expensiveCards.Count} replacement cards, one for each expensive card, in the same order.
You MUST only use card names from the budget pool list above.";

        try
        {
            var rawResponse = await CallOllamaAsync(prompt);
            var jsonContent = ExtractJson(rawResponse);
            return JsonSerializer.Deserialize<List<CardEntry>>(jsonContent, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get budget replacements from Ollama");
            return [];
        }
    }

    // ─── Import Description ───────────────────────────────────────────────────

    public async Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards)
    {
        var cardSample = cards
            .Where(c => c.CardType == null || !c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => $"- {c.Quantity}x {c.Name} ({c.CardType ?? "Unknown"}): {c.RoleInDeck ?? ""}");

        var prompt = $"""
            You are a Magic: The Gathering expert. Based on the following deck card list, write a concise 2-3 sentence
            flavorful description of the deck's playstyle and theme. Respond with ONLY the description text, no JSON, no formatting.

            Deck Name: {deckName}
            Sample Cards:
            {string.Join("\n", cardSample)}
            """;

        try
        {
            return (await CallOllamaAsync(prompt)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate import description via Ollama");
            return $"Imported deck: {deckName}";
        }
    }

    // ─── Ollama HTTP ──────────────────────────────────────────────────────────

    private async Task<string> CallOllamaAsync(string prompt)
    {
        var payload = new
        {
            model = _settings.Model,
            stream = false,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            options = new
            {
                num_predict = _settings.MaxTokens,
                temperature = 0.7
            }
        };

        var json = JsonSerializer.Serialize(payload, OllamaJsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending request to Ollama (model={Model})", _settings.Model);
        using var response = await _httpClient.PostAsync("/api/chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Ollama error {response.StatusCode}: {err}");
        }

        var body = await response.Content.ReadAsStringAsync();
        var ollamaResponse = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOpts);

        return ollamaResponse?.Message?.Content
            ?? throw new InvalidOperationException("Empty Ollama response");
    }

    // ─── Prompt Building ──────────────────────────────────────────────────────

    private static string BuildDeckPrompt(DeckGenerationRequest request, string colorIdentity)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Magic: The Gathering deck building expert. Generate a complete deck configuration as a JSON object.");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine($"- Color Identity: {colorIdentity} ({string.Join(", ", request.Colors)})");
        sb.AppendLine($"- Format: {request.Format}");
        sb.AppendLine($"- Power Level: {request.PowerLevel}");
        sb.AppendLine($"- Budget: {request.BudgetRange}");
        sb.AppendLine();
        sb.AppendLine(GetBudgetGuidance(request.BudgetRange));

        if (!string.IsNullOrEmpty(request.PreferredStrategy))
            sb.AppendLine($"- Preferred Strategy: {request.PreferredStrategy}");

        if (!string.IsNullOrEmpty(request.PreferredCommander))
            sb.AppendLine($"- Preferred Commander: {request.PreferredCommander}");

        if (!string.IsNullOrEmpty(request.AdditionalNotes))
            sb.AppendLine($"- Additional Notes: {request.AdditionalNotes}");

        sb.AppendLine();
        if (request.Format.Equals("Commander", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("CRITICAL REQUIREMENT: Commander decks must contain EXACTLY 100 cards total.");
            sb.AppendLine("Commander is a singleton format — every card must have quantity 1. No exceptions, not even basic lands.");
            sb.AppendLine("The 100 cards must include: 1 commander + 99 other cards (creatures, spells, artifacts, enchantments, and lands).");
            sb.AppendLine("Include approximately 35-38 lands. The cards array MUST have exactly 100 entries, each with quantity 1.");
            sb.AppendLine("Set totalCards to exactly 100.");
        }
        else
        {
            sb.AppendLine("Generate exactly 60 cards for this format.");
        }
        sb.AppendLine();
        sb.AppendLine("""
            Respond with ONLY a valid JSON object (no markdown, no explanation) matching this exact schema:
            {
              "deckName": "string - creative thematic name for the deck",
              "commander": "string - commander card name (or empty for non-Commander formats)",
              "strategy": "string - brief strategy description (2-3 sentences)",
              "estimatedTotalPrice": number,
              "totalCards": number,
              "deckDescription": "string - flavorful 2-3 sentence description of the deck's playstyle and theme",
              "cards": [
                {
                  "name": "string - exact card name",
                  "quantity": number,
                  "manaCost": "string - mana cost like {2}{B}{G}",
                  "cmc": number,
                  "cardType": "string - e.g. Creature - Elf Shaman",
                  "category": "string - one of: Commander, Creature, Instant, Sorcery, Enchantment, Artifact, Planeswalker, Land",
                  "roleInDeck": "string - brief explanation of why this card is in the deck",
                  "estimatedPrice": number
                }
              ]
            }
            """);
        sb.AppendLine();
        sb.AppendLine("Use real Magic: The Gathering card names. Ensure the deck is legal in the specified format.");
        sb.AppendLine("Category the cards into their primary type. Include a good mana base with appropriate lands.");
        sb.AppendLine("Make sure estimated prices are realistic for current market values.");
        sb.AppendLine("IMPORTANT: The estimatedTotalPrice field MUST equal the sum of all individual card estimatedPrice values, and MUST fall within the budget range specified above.");
        sb.AppendLine();
        sb.AppendLine("FINAL CHECK: Before responding, count every entry in your cards array. If you do not have exactly the required number, add or remove cards until you do. Verify the total price stays within the budget range.");

        return sb.ToString();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string ExtractJson(string text)
    {
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

        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        var bracketStart = text.IndexOf('[');
        var bracketEnd = text.LastIndexOf(']');

        if (bracketStart >= 0 && bracketEnd > bracketStart
            && (braceStart < 0 || bracketStart < braceStart))
            return text[bracketStart..(bracketEnd + 1)];

        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return text.Trim();
    }

    private static string RepairTruncatedDeckJson(string json)
    {
        var lastCompleteObject = -1;
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 1)
                    lastCompleteObject = i;
            }
        }

        if (lastCompleteObject > 0)
            return json[..(lastCompleteObject + 1)] + "\n  ]\n}";

        return json;
    }

    private void EnforceCommanderCardCount(DeckConfiguration deck, DeckGenerationRequest request)
    {
        var totalCards = deck.Cards.Sum(c => c.Quantity);
        if (totalCards == 100)
            return;

        _logger.LogWarning(
            "Commander deck generated with {ActualCount} cards instead of 100. Adjusting.",
            totalCards);

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

            for (var i = 0; i < deficit; i++)
            {
                deck.Cards.Add(new CardEntry
                {
                    Name = basicLands[i % basicLands.Count],
                    Quantity = 1,
                    ManaCost = "",
                    Cmc = 0,
                    CardType = "Basic Land",
                    Category = "Land",
                    RoleInDeck = "Mana base (auto-added to reach 100 cards)",
                    EstimatedPrice = 0.25m
                });
            }

            _logger.LogWarning("Padded Commander deck with {Deficit} basic lands to reach 100 cards.", deficit);
        }
    }

    private static string GetBudgetGuidance(string budgetRange)
    {
        if (budgetRange.Contains("under $50", StringComparison.OrdinalIgnoreCase)
            || budgetRange.Equals("Budget", StringComparison.OrdinalIgnoreCase))
        {
            return """
                BUDGET CONSTRAINT (STRICT): The total deck price MUST be under $50.
                Use budget-friendly reprints and commons/uncommons. No card should exceed $2-3.
                Avoid expensive staples — choose affordable alternatives. Most cards should be under $1.
                """;
        }

        if (budgetRange.Contains("$50", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase))
        {
            return """
                BUDGET CONSTRAINT (STRICT): The total deck price MUST be between $50 and $150.
                Most cards should be $0.25-$3. A few key cards can be $5-$10. No single card over $15.
                Use good-quality cards but avoid the most expensive versions of staples.
                """;
        }

        if (budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$500", StringComparison.OrdinalIgnoreCase))
        {
            return """
                BUDGET CONSTRAINT: The total deck price MUST be between $150 and $500.
                You can include powerful staples and quality lands. A few cards can be $20-$40.
                Build a strong, optimized deck while staying within the price range.
                """;
        }

        if (budgetRange.Contains("no budget", StringComparison.OrdinalIgnoreCase)
            || budgetRange.Contains("no limit", StringComparison.OrdinalIgnoreCase))
        {
            return "No budget restriction. Use the best cards available regardless of price.";
        }

        return $"Stay within the specified budget: {budgetRange}.";
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
                _ => null
            };
            if (land != null)
                lands.Add(land);
        }

        if (lands.Count == 0)
            lands.Add("Wastes");

        return lands;
    }

    // ─── Ollama Response DTOs ─────────────────────────────────────────────────

    private class OllamaChatResponse
    {
        public OllamaMessageContent? Message { get; set; }
        public bool Done { get; set; }
    }

    private class OllamaMessageContent
    {
        public string Content { get; set; } = "";
    }
}
