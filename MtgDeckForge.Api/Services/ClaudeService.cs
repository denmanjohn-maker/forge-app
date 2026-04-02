using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
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

        var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseBody);
        var textContent = claudeResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrEmpty(textContent))
            throw new Exception("No text content in Claude response");

        // Extract JSON from the response (Claude may wrap it in markdown code blocks)
        var jsonContent = ExtractJson(textContent);
        
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        
        var deck = JsonSerializer.Deserialize<DeckConfiguration>(jsonContent, options);
        
        if (deck == null)
            throw new Exception("Failed to deserialize deck configuration from Claude response");

        deck.Colors = request.Colors;
        deck.Format = request.Format;
        deck.PowerLevel = request.PowerLevel;
        deck.BudgetRange = request.BudgetRange;

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

        var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseBody);
        var textContent = claudeResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrEmpty(textContent))
            throw new Exception("No text content in Claude response");

        var jsonContent = ExtractJson(textContent);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var analysis = JsonSerializer.Deserialize<DeckAnalysis>(jsonContent, options);

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

            var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseBody);
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

        if (!string.IsNullOrEmpty(request.PreferredStrategy))
            sb.AppendLine($"- Preferred Strategy: {request.PreferredStrategy}");

        if (!string.IsNullOrEmpty(request.PreferredCommander))
            sb.AppendLine($"- Preferred Commander: {request.PreferredCommander}");

        if (!string.IsNullOrEmpty(request.AdditionalNotes))
            sb.AppendLine($"- Additional Notes: {request.AdditionalNotes}");

        sb.AppendLine();
        sb.AppendLine("For Commander format, generate exactly 100 cards (including the commander and basic lands).");
        sb.AppendLine("For other formats, generate 60 cards.");
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
}

// Claude API response models
public class ClaudeResponse
{
    [JsonPropertyName("content")]
    public List<ClaudeContent>? Content { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
