namespace MtgDeckForge.Api.Models;

public class LocalLlmSettings
{
    /// <summary>Base URL of the mtg-forge-local API (deck generation + card search).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Base URL of the Ollama instance (used directly for analysis).</summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>Ollama model name for chat completions.</summary>
    public string Model { get; set; } = "llama3.1:8b";
}
