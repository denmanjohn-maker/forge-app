using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MtgDeckForge.Api.Json;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

public class ClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeApiSettings _settings;
    private readonly ILogger<ClaudeService> _logger;

    public ClaudeService(HttpClient httpClient, IOptions<ClaudeApiSettings> settings, ILogger<ClaudeService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<DeckConfiguration> GenerateDeckAsync(DeckGenerationRequest request)
    {
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

        var prompt = BuildPrompt(request, colorIdentity);

        var apiRequest = new
        {
            model = _settings.Model,
            max_tokens = _settings.MaxTokens,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var json = JsonSerializer.Serialize(apiRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new Exception($"Claude API returned {response.StatusCode}: {responseBody}");
        }

        var claudeResponse = JsonSerializer.Deserialize(responseBody, AppJsonContext.Default.ClaudeResponse);
        var textContent = claudeResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrEmpty(textContent))
            throw new Exception("No text content in Claude response");

        if (claudeResponse?.StopReason == "max_tokens")
            _logger.LogWarning("Claude response was truncated (hit max_tokens limit). Attempting JSON repair.");

        // Extract JSON from the response (Claude may wrap it in markdown code blocks)
        var jsonContent = ExtractJson(textContent);

        DeckConfiguration? deck;
        try
        {
            deck = JsonSerializer.Deserialize(jsonContent, AppJsonContext.Default.DeckConfiguration);
        }
        catch (JsonException ex) when (claudeResponse?.StopReason == "max_tokens")
        {
            _logger.LogWarning(ex, "Truncated JSON detected, attempting repair");
            jsonContent = RepairTruncatedDeckJson(jsonContent);
            deck = JsonSerializer.Deserialize(jsonContent, AppJsonContext.Default.DeckConfiguration);
        }
        
        if (deck == null)
            throw new Exception("Failed to deserialize deck configuration from Claude response");

        deck.Colors = request.Colors;
        deck.Format = request.Format;
        deck.PowerLevel = request.PowerLevel;
        deck.BudgetRange = request.BudgetRange;

        // Enforce correct card count for Commander format
        if (request.Format.Equals("Commander", StringComparison.OrdinalIgnoreCase))
        {
            var totalCards = deck.Cards.Sum(c => c.Quantity);
            if (totalCards != 100)
            {
                _logger.LogWarning(
                    "Commander deck generated with {ActualCount} cards instead of 100. Adjusting.",
                    totalCards);

                // Ensure all cards are singleton (quantity 1) as Commander requires
                foreach (var card in deck.Cards)
                    card.Quantity = 1;

                totalCards = deck.Cards.Count;

                if (totalCards > 100)
                {
                    // Remove excess non-land, non-commander cards from the end
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
                    // Pad with basic lands matching the deck's color identity
                    var deficit = 100 - totalCards;
                    var basicLands = GetBasicLandsForColors(request.Colors);
                    var existingNames = new HashSet<string>(deck.Cards.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < deficit; i++)
                    {
                        var landName = basicLands[i % basicLands.Count];

                        // Commander is singleton — if this land name already exists, skip to next
                        // But basic lands are allowed as duplicates in Commander, so just add them
                        deck.Cards.Add(new CardEntry
                        {
                            Name = landName,
                            Quantity = 1,
                            ManaCost = "",
                            Cmc = 0,
                            CardType = "Basic Land",
                            Category = "Land",
                            RoleInDeck = "Mana base (auto-added to reach 100 cards)",
                            EstimatedPrice = 0.25m
                        });
                    }

                    _logger.LogWarning(
                        "Padded Commander deck with {Deficit} basic lands to reach 100 cards.",
                        deficit);
                }
            }

            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
        }
        else
        {
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
        }

        return deck;
    }

    public async Task<DeckAnalysis> AnalyzeDeckAsync(DeckConfiguration deck)
    {
        var cardList = string.Join("\n", deck.Cards.Select(c =>
            $"- {c.Quantity}x {c.Name} ({c.CardType}, CMC {c.Cmc}): {c.RoleInDeck}"));

        var prompt = $@"You are a Magic: The Gathering deck analysis expert. Analyze the following deck and provide detailed, actionable feedback.

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

        var apiRequest = new
        {
            model = _settings.Model,
            max_tokens = 2000,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(apiRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new Exception($"Claude API returned {response.StatusCode}: {responseBody}");
        }

        var claudeResponse = JsonSerializer.Deserialize(responseBody, AppJsonContext.Default.ClaudeResponse);
        var textContent = claudeResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrEmpty(textContent))
            throw new Exception("No text content in Claude response");

        var jsonContent = ExtractJson(textContent);
        var analysis = JsonSerializer.Deserialize(jsonContent, AppJsonContext.Default.DeckAnalysis);

        if (analysis == null)
            throw new Exception("Failed to deserialize deck analysis from Claude response");

        return analysis;
    }

    public async Task<string> GenerateImportDescriptionAsync(string deckName, List<CardEntry> cards)
    {
        var cardSample = cards
            .Where(c => c.CardType == null || !c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => $"- {c.Quantity}x {c.Name} ({c.CardType ?? "Unknown"}): {c.RoleInDeck ?? ""}");

        var prompt = $@"You are a Magic: The Gathering expert. Based on the following deck card list, write a concise 2-3 sentence flavorful description of the deck's playstyle and theme. Respond with ONLY the description text, no JSON, no formatting.

Deck Name: {deckName}
Sample Cards:
{string.Join("\n", cardSample)}";

        var apiRequest = new
        {
            model = _settings.Model,
            max_tokens = 300,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(apiRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return $"Imported deck: {deckName}";

            var claudeResponse = JsonSerializer.Deserialize(responseBody, AppJsonContext.Default.ClaudeResponse);
            return claudeResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text?.Trim()
                ?? $"Imported deck: {deckName}";
        }
        catch
        {
            return $"Imported deck: {deckName}";
        }
    }

    private string BuildPrompt(DeckGenerationRequest request, string colorIdentity)
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
        sb.AppendLine("Respond with ONLY a valid JSON object (no markdown, no explanation) matching this exact schema:");
        sb.AppendLine(@"{
  ""deckName"": ""string - creative thematic name for the deck"",
  ""commander"": ""string - commander card name (or empty for non-Commander formats)"",
  ""strategy"": ""string - brief strategy description (2-3 sentences)"",
  ""estimatedTotalPrice"": number,
  ""totalCards"": number,
  ""deckDescription"": ""string - flavorful 2-3 sentence description of the deck's playstyle and theme"",
  ""cards"": [
    {
      ""name"": ""string - exact card name"",
      ""quantity"": number,
      ""manaCost"": ""string - mana cost like {2}{B}{G}"",
      ""cmc"": number,
      ""cardType"": ""string - e.g. Creature - Elf Shaman"",
      ""category"": ""string - one of: Commander, Creature, Instant, Sorcery, Enchantment, Artifact, Planeswalker, Land"",
      ""roleInDeck"": ""string - brief explanation of why this card is in the deck"",
      ""estimatedPrice"": number
    }
  ]
}");
        sb.AppendLine();
        sb.AppendLine("Use real Magic: The Gathering card names. Ensure the deck is legal in the specified format.");
        sb.AppendLine("Category the cards into their primary type. Include a good mana base with appropriate lands.");
        sb.AppendLine("Make sure estimated prices are realistic for current market values.");
        sb.AppendLine("IMPORTANT: The estimatedTotalPrice field MUST equal the sum of all individual card estimatedPrice values, and MUST fall within the budget range specified above.");
        sb.AppendLine();
        sb.AppendLine("FINAL CHECK: Before responding, count every entry in your cards array. If you do not have exactly the required number, add or remove cards until you do. Verify the total price stays within the budget range.");

        return sb.ToString();
    }

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

        // Try to find raw JSON object
        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return text.Trim();
    }

    private static string GetBudgetGuidance(string budgetRange)
    {
        if (budgetRange.Contains("under $50", StringComparison.OrdinalIgnoreCase)
            || budgetRange.Equals("Budget", StringComparison.OrdinalIgnoreCase))
        {
            return @"BUDGET CONSTRAINT (STRICT): The total deck price MUST be under $50. 
Use budget-friendly reprints and commons/uncommons. No card should exceed $2-3. 
Avoid expensive staples — choose affordable alternatives. Most cards should be under $1.";
        }

        if (budgetRange.Contains("$50", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase))
        {
            return @"BUDGET CONSTRAINT (STRICT): The total deck price MUST be between $50 and $150.
Most cards should be $0.25-$3. A few key cards can be $5-$10. No single card over $15.
Use good-quality cards but avoid the most expensive versions of staples.";
        }

        if (budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$500", StringComparison.OrdinalIgnoreCase))
        {
            return @"BUDGET CONSTRAINT: The total deck price MUST be between $150 and $500.
You can include powerful staples and quality lands. A few cards can be $20-$40.
Build a strong, optimized deck while staying within the price range.";
        }

        if (budgetRange.Contains("no budget", StringComparison.OrdinalIgnoreCase)
            || budgetRange.Contains("no limit", StringComparison.OrdinalIgnoreCase))
        {
            return "No budget restriction. Use the best cards available regardless of price.";
        }

        return $"Stay within the specified budget: {budgetRange}.";
    }

    /// <summary>
    /// Attempts to repair truncated JSON from Claude by removing the incomplete last card entry
    /// and closing the cards array and root object.
    /// </summary>
    private static string RepairTruncatedDeckJson(string json)
    {
        // Find the last complete card object (last '}' followed by a comma or within the array)
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
                // depth == 1 means we just closed a card object (inside the root object's cards array)
                if (depth == 1)
                    lastCompleteObject = i;
            }
        }

        if (lastCompleteObject > 0)
        {
            // Truncate after the last complete card object, close the array and root object
            var repaired = json[..(lastCompleteObject + 1)] + "\n  ]\n}";
            return repaired;
        }

        return json;
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

        // Fallback for colorless or unrecognized
        if (lands.Count == 0)
            lands.Add("Wastes");

        return lands;
    }
}

// Claude API response models
public class ClaudeResponse
{
    [JsonPropertyName("content")]
    public List<ClaudeContent>? Content { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
