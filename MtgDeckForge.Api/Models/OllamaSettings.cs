namespace MtgDeckForge.Api.Models;

public class OllamaSettings
{
    /// <summary>Base URL of the Ollama instance (e.g. http://ollama.railway.internal:11434).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Ollama model name for chat completions.</summary>
    public string Model { get; set; } = "mistral";

    /// <summary>Maximum output tokens.</summary>
    public int MaxTokens { get; set; } = 8192;
}
