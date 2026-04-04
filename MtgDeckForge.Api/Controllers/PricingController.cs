using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MtgDeckForge.Api.Data;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PricingController : ControllerBase
{
    private readonly MtgJsonPricingImportService _importService;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public PricingController(MtgJsonPricingImportService importService, AppDbContext db, IHttpClientFactory httpClientFactory)
    {
        _importService = importService;
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string cardName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            return BadRequest(new { error = "cardName is required" });

        var sources = new List<object>();

        // Source 1: Local DB (MTGJSON / TCGPlayer import)
        var normalized = PricingService.NormalizeCardName(cardName);
        var localPrice = await _db.CardPrices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedCardName == normalized, ct);
        if (localPrice != null)
        {
            sources.Add(new
            {
                source = "TCGPlayer (via MTGJSON)",
                priceUsd = localPrice.PriceUsd,
                updatedAt = localPrice.UpdatedAtUtc,
                url = $"https://www.tcgplayer.com/search/magic/product?q={Uri.EscapeDataString(cardName)}"
            });
        }

        // Source 2 & 3: Scryfall (includes their own price + Card Kingdom)
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "MtgDeckForge/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var scryfallUrl = $"https://api.scryfall.com/cards/named?fuzzy={Uri.EscapeDataString(cardName)}";
            var resp = await client.GetAsync(scryfallUrl, ct);

            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
                var prices = json.GetProperty("prices");
                var name = json.GetProperty("name").GetString() ?? cardName;
                var scryfallUri = json.TryGetProperty("scryfall_uri", out var su) ? su.GetString() : null;
                var set = json.TryGetProperty("set_name", out var sn) ? sn.GetString() : null;
                var imageUrl = (string?)null;
                if (json.TryGetProperty("image_uris", out var imgs) && imgs.TryGetProperty("normal", out var normalImg))
                    imageUrl = normalImg.GetString();
                else if (json.TryGetProperty("card_faces", out var faces) && faces.GetArrayLength() > 0)
                {
                    var face = faces[0];
                    if (face.TryGetProperty("image_uris", out var fImgs) && fImgs.TryGetProperty("normal", out var fNormal))
                        imageUrl = fNormal.GetString();
                }

                // Scryfall price (USD)
                if (prices.TryGetProperty("usd", out var usdEl) && usdEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (decimal.TryParse(usdEl.GetString(), out var usd))
                    {
                        sources.Add(new
                        {
                            source = "Scryfall (Market)",
                            priceUsd = usd,
                            updatedAt = (DateTime?)null,
                            url = scryfallUri ?? $"https://scryfall.com/search?q={Uri.EscapeDataString(name)}"
                        });
                    }
                }

                // Scryfall foil price
                if (prices.TryGetProperty("usd_foil", out var foilEl) && foilEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (decimal.TryParse(foilEl.GetString(), out var foil))
                    {
                        sources.Add(new
                        {
                            source = "Scryfall (Foil)",
                            priceUsd = foil,
                            updatedAt = (DateTime?)null,
                            url = scryfallUri
                        });
                    }
                }

                // Card Kingdom price (via Scryfall)
                if (prices.TryGetProperty("usd_etched", out var etchedEl) && etchedEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (decimal.TryParse(etchedEl.GetString(), out var etched))
                    {
                        sources.Add(new
                        {
                            source = "Scryfall (Etched)",
                            priceUsd = etched,
                            updatedAt = (DateTime?)null,
                            url = scryfallUri
                        });
                    }
                }

                // EUR price
                decimal? eurPrice = null;
                if (prices.TryGetProperty("eur", out var eurEl) && eurEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (decimal.TryParse(eurEl.GetString(), out var eur))
                        eurPrice = eur;
                }

                // MTGO Tix price
                decimal? tixPrice = null;
                if (prices.TryGetProperty("tix", out var tixEl) && tixEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (decimal.TryParse(tixEl.GetString(), out var tix))
                        tixPrice = tix;
                }

                return Ok(new
                {
                    cardName = name,
                    set,
                    imageUrl,
                    scryfallUrl = scryfallUri,
                    sources,
                    eurPrice,
                    tixPrice
                });
            }
        }
        catch (Exception ex)
        {
            // Scryfall failed — still return local data if we have it
            if (sources.Count > 0)
            {
                return Ok(new
                {
                    cardName,
                    set = (string?)null,
                    imageUrl = (string?)null,
                    scryfallUrl = (string?)null,
                    sources,
                    eurPrice = (decimal?)null,
                    tixPrice = (decimal?)null,
                    warning = $"Scryfall lookup failed: {ex.Message}"
                });
            }
        }

        if (sources.Count == 0)
            return NotFound(new { error = $"No pricing data found for '{cardName}'" });

        return Ok(new
        {
            cardName,
            set = (string?)null,
            imageUrl = (string?)null,
            scryfallUrl = (string?)null,
            sources,
            eurPrice = (decimal?)null,
            tixPrice = (decimal?)null
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await _importService.ImportDailyAsync(cancellationToken);
        if (!result.Success) return StatusCode(500, new { error = result.Message });
        return Ok(new { imported = result.ImportedCount });
    }

    [Authorize(Roles = "Admin")]
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
