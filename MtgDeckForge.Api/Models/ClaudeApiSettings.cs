namespace MtgDeckForge.Api.Models;

public class ClaudeApiSettings
{
    public string ApiKey { get; set; } = null!;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 4096;
}
