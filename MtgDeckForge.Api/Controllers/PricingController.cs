using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class PricingController : ControllerBase
{
    private readonly MtgJsonPricingImportService _importService;

    public PricingController(MtgJsonPricingImportService importService)
    {
        _importService = importService;
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await _importService.ImportDailyAsync(cancellationToken);
        if (!result.Success) return StatusCode(500, new { error = result.Message });
        return Ok(new { imported = result.ImportedCount });
    }
}
