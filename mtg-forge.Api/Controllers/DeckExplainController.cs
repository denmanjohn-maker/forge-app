using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/decks")]
public class DeckExplainController : ControllerBase
{
    private readonly DeckService _deckService;
    private readonly RagPipelineService _ragService;

    public DeckExplainController(DeckService deckService, RagPipelineService ragService)
    {
        _deckService = deckService;
        _ragService = ragService;
    }

    [HttpPost("{id}/explain")]
    [Authorize]
    public async Task<IActionResult> ExplainDeck(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (deck.UserId != userId && deck.Id != null && !deck.Id.EndsWith("example") && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var explanation = await _ragService.ExplainDeckAsync(deck);
        return Ok(explanation);
    }
}
