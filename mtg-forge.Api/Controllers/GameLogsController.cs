using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameLogsController : ControllerBase
{
    private readonly GameLogService _gameLogService;
    private readonly DeckService _deckService;

    public GameLogsController(GameLogService gameLogService, DeckService deckService)
    {
        _gameLogService = gameLogService;
        _deckService = deckService;
    }

    [HttpGet("deck/{deckId}")]
    public async Task<IActionResult> GetByDeck(string deckId)
    {
        var logs = await _gameLogService.GetByDeckAsync(deckId);
        return Ok(logs);
    }

    [HttpGet("deck/{deckId}/stats")]
    public async Task<IActionResult> GetStats(string deckId)
    {
        var stats = await _gameLogService.GetWinRateAsync(deckId);
        return Ok(stats);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGameLogRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.DeckId))
            return BadRequest("DeckId is required");
        if (string.IsNullOrWhiteSpace(request.Result))
            return BadRequest("Result is required");

        var deck = await _deckService.GetByIdAsync(request.DeckId);
        if (deck == null) return NotFound("Deck not found");
        if (deck.UserId != userId && !User.IsInRole("Admin")) return Forbid();

        var now = DateTime.UtcNow;
        var log = new GameLog
        {
            UserId = userId,
            DeckId = request.DeckId,
            Result = request.Result,
            OpponentArchetype = request.OpponentArchetype,
            OpponentDeckId = string.IsNullOrWhiteSpace(request.OpponentDeckId) ? null : request.OpponentDeckId,
            Notes = request.Notes,
            Format = string.IsNullOrWhiteSpace(request.Format) ? "Commander" : request.Format,
            GameNumber = request.GameNumber,
            TurnCount = request.TurnCount,
            MulliganCount = request.MulliganCount,
            Date = now,
            CreatedAt = now
        };

        var created = await _gameLogService.CreateAsync(log);

        return CreatedAtAction(nameof(GetByDeck), new { deckId = created.DeckId }, created);
    }

    [HttpDelete("{id}/{deckId}")]
    public async Task<IActionResult> Delete(string id, string deckId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var deck = await _deckService.GetByIdAsync(deckId);
        if (deck == null) return NotFound("Deck not found");
        if (deck.UserId != userId && !User.IsInRole("Admin")) return Forbid();

        await _gameLogService.DeleteAsync(id, deckId);
        return NoContent();
    }
}
