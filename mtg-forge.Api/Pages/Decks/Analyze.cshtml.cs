using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgForge.Api.Services;

namespace MtgForge.Api.Pages.Decks;

public class AnalyzeModel : PageModel
{
    private readonly DeckService _deckService;
    private readonly ClaudeService _claudeService;

    public AnalyzeModel(DeckService deckService, ClaudeService claudeService)
    {
        _deckService = deckService;
        _claudeService = claudeService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public string? AnalysisJson { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var deck = await _deckService.GetByIdAsync(Id);
        if (deck is null) return NotFound();
        if (deck.LastAnalysis is not null)
            AnalysisJson = JsonSerializer.Serialize(deck.LastAnalysis, new JsonSerializerOptions { WriteIndented = true });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deck = await _deckService.GetByIdAsync(Id);
        if (deck is null) return NotFound();

        var analysis = await _claudeService.AnalyzeDeckAsync(deck);
        await _deckService.UpdateAnalysisAsync(Id, analysis);
        AnalysisJson = JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true });
        return Page();
    }
}
