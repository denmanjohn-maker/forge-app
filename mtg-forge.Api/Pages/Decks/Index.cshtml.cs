using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgForge.Api.Models;
using MtgForge.Api.Services;
using System.Security.Claims;

namespace MtgForge.Api.Pages.Decks;

public class IndexModel : PageModel
{
    private readonly DeckService _deckService;

    public IndexModel(DeckService deckService)
    {
        _deckService = deckService;
    }

    public List<DeckConfiguration> Decks { get; set; } = new();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var result = await _deckService.GetPagedAsync(userId, isAdmin, name: null, color: null, format: null, powerLevel: null, skip: 0, limit: 50);
        Decks = result.Items;
    }
}
