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

    public async Task<string> BrewWithAiAsync(AiChatSession session, string prompt)
    {
        if (string.IsNullOrWhiteSpace(_settings.LlmApiKey))
            throw new InvalidOperationException(
                "RagPipeline:LlmApiKey is required. Set it via environment variable RAGPIPELINE__LLMAPIKEY.");

        const string systemPrompt =
            "You are 'Forge AI' - a collaborative Magic: The Gathering deck building assistant. " +
            "You are conversing with a player and helping them refine their deck strategy. " +
            "Be conversational, helpful, and concise.";

        // Build the conversation: system + prior turns + the new user prompt.
        var messages = session.Messages
            .Select(m => new { role = m.Role, content = m.Content })
            .Prepend(new { role = "system", content = systemPrompt })
            .Append(new { role = "user", content = prompt })
            .ToList();

        var payload = new
        {
            model = _settings.Model,
            stream = false,
            max_tokens = 1000,
            temperature = 0.7,
            messages
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

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Empty response from LLM.");
    }
}
