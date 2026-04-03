using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Pages.Decks;

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
