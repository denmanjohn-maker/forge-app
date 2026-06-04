using System.Text.Json;
using System.Text.Json.Serialization;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

// Extending RagPipelineService with new AI capabilities using a partial class to avoid
// modifying the massive existing file directly.
public partial class RagPipelineService
{
    public async Task<DeckExplanation> ExplainDeckAsync(DeckConfiguration deck)
    {
        var schema = """
            {
              "summary": "string - brief 2-3 sentence overview",
              "howItPlays": "string - paragraph on the general gameplan",
              "keyCombos": "string - list of important card combinations",
              "mulliganStrategy": "string - what to look for in opening hand"
            }
            """;

        var cardList = string.Join("\n", deck.Cards.Select(c =>
            $"- {c.Quantity}x {c.Name} ({c.CardType})"));

        var systemPrompt = $"""
            You are an expert Magic: The Gathering strategy coach.
            Explain the following {deck.Format} deck commanded by {deck.Commander}.
            Respond ONLY with a valid JSON object matching this schema:
            {schema}
            """;

        var userPrompt = $"Deck Name: {deck.DeckName}\nStrategy: {deck.Strategy}\nCards:\n{cardList}";

        var responseJson = await CallLlmAsync(systemPrompt, userPrompt, jsonMode: true, temperature: 0.4, operation: "explain_deck", deckId: deck.Id, format: deck.Format);

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new Exception("Llm returned an empty response for deck explanation.");
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<DeckExplanation>(responseJson, options)
                ?? throw new Exception("Failed to deserialize explanation.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse explanation JSON from LLM: {Json}", responseJson);
            throw new Exception("The AI returned invalid JSON for the explanation.");
        }
    }

    private class AiBrewResult
    {
        public string Reply { get; set; } = string.Empty;
        public List<AiChatAction>? Actions { get; set; }
    }

    public async Task<(string Reply, List<AiChatAction>? Actions)> BrewWithAiAsync(AiChatSession session, string prompt, User? user, DeckConfiguration? deck)
    {
        if (string.IsNullOrWhiteSpace(_settings.LlmApiKey))
            throw new InvalidOperationException(
                "RagPipeline:LlmApiKey is required. Set it via environment variable RAGPIPELINE__LLMAPIKEY.");

        var systemPrompt = "You are 'Forge AI' - a collaborative Magic: The Gathering deck building assistant. " +
                           "You are conversing with a player and helping them refine their deck strategy. " +
                           "Be conversational, helpful, and concise. " +
                           "You must ALWAYS return your response as a SINGLE valid JSON object (NOT an array) with a 'reply' property containing your conversational markdown string, and an 'actions' property containing a list of actions the user can take.\n" +
                           "CRITICAL: Be proactive. When discussing potential changes or asking what the user wants to do next, ALWAYS present them with 1 to 3 concrete options as actionable buttons.\n" +
                           "CRITICAL: If you suggest adding, removing, or swapping specific cards, you MUST ALWAYS include those operations in the 'actions' array so the user can click them.\n" +
                           "CRITICAL: The 'type' property of an action MUST ONLY be exactly 'add', 'swap', or 'reply'. Do not invent new types (e.g. do not use 'replace').\n" +
                           "Example JSON response:\n" +
                           "{\n" +
                           "  \"reply\": \"What do you want to focus on next?\",\n" +
                           "  \"actions\": [\n" +
                           "    { \"type\": \"add\", \"addCard\": \"Sol Ring\", \"label\": \"Add Sol Ring\" },\n" +
                           "    { \"type\": \"swap\", \"removeCard\": \"Forest\", \"addCard\": \"Arcane Signet\", \"label\": \"Swap Forest for Arcane Signet\" },\n" +
                           "    { \"type\": \"reply\", \"message\": \"I want to improve my mana ramp.\", \"label\": \"Option 1: Focus on Ramp\" }\n" +
                           "  ]\n" +
                           "}";

        if (user != null)
        {
            systemPrompt += $"\n\nYou are talking to: {user.DisplayName}.";
        }

        if (deck != null)
        {
            systemPrompt += $"\n\nThe user is currently working on the following deck:\n" +
                            $"- Deck Name: {deck.DeckName}\n" +
                            $"- Commander: {deck.Commander}\n" +
                            $"- Format: {deck.Format}\n" +
                            $"- Strategy: {deck.Strategy}\n" +
                            $"- Power Level: {deck.PowerLevel}\n" +
                            $"- Colors: {string.Join(", ", deck.Colors)}\n\n" +
                            "Decklist:\n";

            foreach (var card in deck.Cards)
            {
                systemPrompt += $"- {card.Quantity}x {card.Name} ({card.Category})\n";
            }
        }

        // Build the conversation: system + prior turns + the new user prompt.
        var messages = session.Messages
            .Select(m => new 
            { 
                role = m.Role, 
                content = m.Role == "assistant" 
                    ? JsonSerializer.Serialize(new AiBrewResult { Reply = m.Content, Actions = m.Actions }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })
                    : m.Content 
            })
            .Prepend(new { role = "system", content = systemPrompt })
            .Append(new { role = "user", content = prompt })
            .ToList();

        var payload = new
        {
            model = _settings.Model,
            stream = false,
            max_tokens = 1000,
            temperature = 0.7,
            messages,
            response_format = new { type = "json_object" }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // Mirror CallLlmAsync's client setup so the chat path cannot drift from the
        // proven analysis path — in particular the 5-minute timeout (the default
        // HttpClient timeout of 100s can be exceeded by large LLM responses).
        var client = _factory.CreateClient();
        client.BaseAddress = new Uri(_settings.LlmBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.LlmApiKey);

        using var reqContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/chat/completions", reqContent);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("BrewWithAiAsync: LLM returned {Status} — {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"LLM returned error {response.StatusCode}: {err}");
        }

        var body = await response.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        var contentString = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Empty response from LLM.");

        try 
        {
            var cleanedContent = contentString.Trim();
            if (cleanedContent.StartsWith("[") && cleanedContent.EndsWith("]"))
            {
                var listResult = JsonSerializer.Deserialize<List<AiBrewResult>>(cleanedContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var first = listResult?.FirstOrDefault();
                return (first?.Reply ?? "No reply", first?.Actions);
            }

            var brewResult = JsonSerializer.Deserialize<AiBrewResult>(cleanedContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return (brewResult?.Reply ?? "No reply", brewResult?.Actions);
        }
        catch(Exception e)
        {
            _logger.LogError(e, "Failed to parse json from llm brew response. Content: {ContentString}", contentString);
            return (contentString, null);
        }
    }
}
