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
        // Build prior messages into context
        var systemPrompt = "You are 'Forge AI' - a collaborative Magic: The Gathering deck building assistant. " +
            "You are conversing with a player and helping them refine their deck strategy. " +
            "Be conversational, helpful, and concise.";

        var requestBody = new
        {
            model = _settings.Model,
            messages = new List<object> { new { role = "system", content = systemPrompt } }
                .Concat(session.Messages.Select(m => new { role = m.Role, content = m.Content }))
                .Concat(new[] { new { role = "user", content = prompt } }),
            temperature = 0.7,
            max_tokens = 1000
        };

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.LlmApiKey);
        var url = $"{_settings.LlmBaseUrl.TrimEnd('/')}/chat/completions";

        var reqContent = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, reqContent);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var assistantMsg = jsonDoc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return assistantMsg ?? "Error generating response.";
    }
}
