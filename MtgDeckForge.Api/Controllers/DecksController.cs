using Microsoft.AspNetCore.Mvc;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DecksController : ControllerBase
{
    private readonly DeckService _deckService;
    private readonly ClaudeService _claudeService;
    private readonly ILogger<DecksController> _logger;

    public DecksController(DeckService deckService, ClaudeService claudeService, ILogger<DecksController> logger)
    {
        _deckService = deckService;
        _claudeService = claudeService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<DeckConfiguration>>> GetAll()
    {
        var decks = await _deckService.GetAllAsync();
        return Ok(decks);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeckConfiguration>> GetById(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        return Ok(deck);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<DeckConfiguration>>> Search([FromQuery] string? color, [FromQuery] string? format)
    {
        var decks = await _deckService.SearchAsync(color, format);
        return Ok(decks);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<DeckConfiguration>> Generate([FromBody] DeckGenerationRequest request)
    {
        try
        {
            _logger.LogInformation("Generating deck with colors: {Colors}, format: {Format}", 
                string.Join(",", request.Colors), request.Format);

            var deck = await _claudeService.GenerateDeckAsync(request);
            var saved = await _deckService.CreateAsync(deck);

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate deck");
            return StatusCode(500, new { error = "Failed to generate deck", details = ex.Message });
        }
    }

    [HttpPost("{id}/analyze")]
    public async Task<ActionResult<DeckAnalysis>> Analyze(string id)
    {
        try
        {
            var deck = await _deckService.GetByIdAsync(id);
            if (deck is null)
                return NotFound();

            _logger.LogInformation("Analyzing deck {Id}: {Name}", id, deck.DeckName);
            var analysis = await _claudeService.AnalyzeDeckAsync(deck);
            return Ok(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze deck {Id}", id);
            return StatusCode(500, new { error = "Failed to analyze deck", details = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _deckService.DeleteAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}
