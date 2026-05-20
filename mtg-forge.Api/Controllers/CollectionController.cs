using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CollectionController : ControllerBase
{
    private readonly CollectionService _collectionService;
    private readonly DeckService _deckService;
    private readonly ILogger<CollectionController> _logger;

    public CollectionController(CollectionService collectionService, DeckService deckService, ILogger<CollectionController> logger)
    {
        _collectionService = collectionService;
        _deckService = deckService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<PagedResult<CollectionEntry>>> GetCollection(
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var result = await _collectionService.GetPagedAsync(GetUserId(), search, skip, limit);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CollectionEntry>> AddCard([FromBody] CollectionAddRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CardName))
            return BadRequest(new { error = "CardName is required" });

        req.Quantity = Math.Max(1, req.Quantity);
        var entry = await _collectionService.AddAsync(GetUserId(), req);
        return Ok(entry);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateCard(string id, [FromBody] CollectionUpdateRequest req)
    {
        var updated = await _collectionService.UpdateAsync(GetUserId(), id, req);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCard(string id)
    {
        var deleted = await _collectionService.DeleteAsync(GetUserId(), id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("import/csv")]
    public async Task<IActionResult> ImportCsv(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();
            var count = await _collectionService.BulkImportFromCsvAsync(GetUserId(), content);
            return Ok(new { imported = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collection CSV import failed");
            return StatusCode(500, new { error = "Import failed" });
        }
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var csv = await _collectionService.ExportToCsvAsync(GetUserId());
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "my-collection.csv");
    }

    [HttpGet("buildable-decks")]
    public async Task<ActionResult<List<BuildableDeck>>> GetBuildableDecks()
    {
        var owned = await _collectionService.GetOwnedQuantitiesAsync(GetUserId());
        if (owned.Count == 0)
            return Ok(new List<BuildableDeck>());

        var decks = await _deckService.GetByUserIdAsync(GetUserId());
        var results = new List<BuildableDeck>();

        foreach (var deck in decks)
        {
            if (deck.Cards.Count == 0) continue;

            int ownedCount = 0;
            var totalCards = deck.Cards.Sum(c => c.Quantity);
            decimal acquisitionCost = 0;
            int missingCount = 0;

            foreach (var card in deck.Cards)
            {
                owned.TryGetValue(card.Name, out var ownedQty);
                var need = Math.Max(0, card.Quantity - ownedQty);
                ownedCount += card.Quantity - need;
                if (need > 0)
                {
                    acquisitionCost += need * card.EstimatedPrice;
                    missingCount += need;
                }
            }

            var pct = totalCards > 0 ? Math.Round((decimal)ownedCount / totalCards * 100, 1) : 0m;

            results.Add(new BuildableDeck
            {
                DeckId = deck.Id!,
                DeckName = deck.DeckName,
                Commander = deck.Commander,
                Format = deck.Format,
                Colors = deck.Colors,
                TotalCards = totalCards,
                OwnedCount = ownedCount,
                CompletionPct = pct,
                MissingCount = missingCount,
                AcquisitionCost = acquisitionCost
            });
        }

        return Ok(results.OrderByDescending(d => d.CompletionPct).ToList());
    }
}
