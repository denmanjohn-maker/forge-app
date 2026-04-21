namespace MtgForge.Api.Models;

/// <summary>
/// Configuration for the RAG pipeline provider (mtg-forge-ai + Together.ai).
/// mtg-forge-ai uses Qdrant vector search to pre-filter cards by price and
/// color identity before passing them to the LLM, solving budget compliance
/// and card legality problems upstream.
/// </summary>
public class RagPipelineSettings
{
    /// <summary>Base URL of the mtg-forge-ai API (deck generation + card search).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5001";

    /// <summary>Base URL of the OpenAI-compatible LLM API used for analysis (Together.ai by default).</summary>
    public string LlmBaseUrl { get; set; } = "https://api.together.xyz";

    /// <summary>Bearer API key for the LLM provider.</summary>
    public string LlmApiKey { get; set; } = "";

    /// <summary>Model name for chat completions.</summary>
    public string Model { get; set; } = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
}
