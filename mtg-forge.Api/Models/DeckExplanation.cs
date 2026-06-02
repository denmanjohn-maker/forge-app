namespace MtgForge.Api.Models;

public class DeckExplanation
{
    public string Summary { get; set; } = null!;
    public string HowItPlays { get; set; } = null!;
    public string KeyCombos { get; set; } = null!;
    public string MulliganStrategy { get; set; } = null!;
}
