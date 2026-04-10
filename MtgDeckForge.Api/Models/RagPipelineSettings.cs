namespace MtgDeckForge.Api.Models;

/// <summary>
/// Configuration for the RAG pipeline provider (mtg-forge-ai + Ollama).
/// mtg-forge-ai uses Qdrant vector search to pre-filter cards by price and
/// color identity before passing them to the LLM, solving budget compliance
/// and card legality problems upstream.
/// </summary>
public class RagPipelineSettings
{
    /// <summary>Base URL of the mtg-forge-ai API (deck generation + card search).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5001";

    /// <summary>Base URL of the Ollama instance (used directly for analysis).</summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>Ollama model name for chat completions.</summary>
    public string Model { get; set; } = "llama3.1:8b";
}
