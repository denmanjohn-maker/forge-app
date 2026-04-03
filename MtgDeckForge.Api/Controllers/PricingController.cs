using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MtgDeckForge.Api.Data;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class PricingController : ControllerBase
{
    private readonly MtgJsonPricingImportService _importService;
    private readonly AppDbContext _db;

    public PricingController(MtgJsonPricingImportService importService, AppDbContext db)
    {
        _importService = importService;
        _db = db;
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await _importService.ImportDailyAsync(cancellationToken);
        if (!result.Success) return StatusCode(500, new { error = result.Message });
        return Ok(new { imported = result.ImportedCount });
    }

    [HttpGet("import-runs")]
    public async Task<IActionResult> GetImportRuns([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var runs = await _db.PricingImportRuns
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(limit)
            .Select(r => new
            {
                r.Id,
                r.StartedAtUtc,
                r.CompletedAtUtc,
                r.Success,
                r.ImportedCount,
                r.Message
            })
            .ToListAsync(cancellationToken);

        var totalPrices = await _db.CardPrices.CountAsync(cancellationToken);

        return Ok(new { runs, totalPricesInDb = totalPrices });
    }
}
