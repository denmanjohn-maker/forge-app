using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Pages.Decks;

public class DetailsModel : PageModel
{
    private readonly DeckService _deckService;

    public DetailsModel(DeckService deckService)
    {
        _deckService = deckService;
    }

    public DeckConfiguration? Deck { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        Deck = await _deckService.GetByIdAsync(id);
        if (Deck is null) return NotFound();
        return Page();
    }
}
