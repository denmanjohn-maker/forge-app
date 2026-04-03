using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MtgDeckForge.Api.Data;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

public class MtgJsonPricingImportService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MtgJsonPricingImportService> _logger;
    private readonly AppDbContext _db;
    private readonly MtgJsonSettings _settings;

    public MtgJsonPricingImportService(HttpClient httpClient, ILogger<MtgJsonPricingImportService> logger, AppDbContext db, IOptions<MtgJsonSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
        _settings = settings.Value;
    }

    public async Task<(bool Success, int ImportedCount, string Message)> ImportDailyAsync(CancellationToken cancellationToken = default)
    {
        var run = new PricingImportRun { StartedAtUtc = DateTime.UtcNow };
        _db.PricingImportRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            using var priceRes = await _httpClient.GetAsync(_settings.PricesUrl, cancellationToken);
            priceRes.EnsureSuccessStatusCode();
            var pricesJson = await priceRes.Content.ReadAsStringAsync(cancellationToken);

            using var printingsRes = await _httpClient.GetAsync(_settings.PrintingsUrl, cancellationToken);
            printingsRes.EnsureSuccessStatusCode();
            var printingsJson = await printingsRes.Content.ReadAsStringAsync(cancellationToken);

            var uuidToName = ParseUuidToName(printingsJson);
            var uuidToUsd = ParseUuidToUsd(pricesJson);

            var imported = 0;
            var now = DateTime.UtcNow;
            var normalizedNames = uuidToUsd.Keys
                .Where(uuidToName.ContainsKey)
                .Select(uuid => PricingService.NormalizeCardName(uuidToName[uuid]))
                .Distinct()
                .ToList();
            var existingByName = await _db.CardPrices
                .Where(x => normalizedNames.Contains(x.NormalizedCardName))
                .ToDictionaryAsync(x => x.NormalizedCardName, cancellationToken);

            foreach (var (uuid, usd) in uuidToUsd)
            {
                if (!uuidToName.TryGetValue(uuid, out var name)) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var normalized = PricingService.NormalizeCardName(name);
                existingByName.TryGetValue(normalized, out var existing);
                if (existing is null)
                {
                    existing = new CardPrice
                    {
                        CardName = name,
                        NormalizedCardName = normalized,
                        SourceUuid = uuid,
                        PriceUsd = usd,
                        UpdatedAtUtc = now
                    };
                    _db.CardPrices.Add(existing);
                    existingByName[normalized] = existing;
                    imported++;
                }
                else
                {
                    var changed = false;
                    if (!string.Equals(existing.CardName, name, StringComparison.Ordinal))
                    {
                        existing.CardName = name;
                        changed = true;
                    }
                    if (!string.Equals(existing.SourceUuid, uuid, StringComparison.Ordinal))
                    {
                        existing.SourceUuid = uuid;
                        changed = true;
                    }
                    if (existing.PriceUsd != usd)
                    {
                        existing.PriceUsd = usd;
                        changed = true;
                    }
                    if (changed)
                    {
                        existing.UpdatedAtUtc = now;
                        imported++;
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

            run.Success = true;
            run.ImportedCount = imported;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Message = "OK";
            await _db.SaveChangesAsync(cancellationToken);

            return (true, imported, "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MTGJSON pricing import failed");
            run.Success = false;
            run.ImportedCount = 0;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Message = ex.Message;
            await _db.SaveChangesAsync(cancellationToken);
            return (false, 0, ex.Message);
        }
    }

    private static Dictionary<string, string> ParseUuidToName(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return dict;

        foreach (var set in data.EnumerateObject())
        {
            if (!set.Value.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array) continue;
            foreach (var card in cards.EnumerateArray())
            {
                if (!card.TryGetProperty("uuid", out var uuidEl)) continue;
                if (!card.TryGetProperty("name", out var nameEl)) continue;
                var uuid = uuidEl.GetString();
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name)) continue;
                dict[uuid] = name;
            }
        }

        return dict;
    }

    private static Dictionary<string, decimal> ParseUuidToUsd(string json)
    {
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return dict;

        foreach (var item in data.EnumerateObject())
        {
            var uuid = item.Name;
            var cardPriceNode = item.Value;

            if (TryGetUsd(cardPriceNode, out var usd))
            {
                dict[uuid] = usd;
                continue;
            }

            foreach (var provider in cardPriceNode.EnumerateObject())
            {
                if (TryGetUsd(provider.Value, out usd))
                {
                    dict[uuid] = usd;
                    break;
                }
            }
        }

        return dict;
    }

    private static bool TryGetUsd(JsonElement node, out decimal usd)
    {
        usd = 0m;
        if (node.ValueKind != JsonValueKind.Object) return false;

        if (node.TryGetProperty("paper", out var paper) && paper.ValueKind == JsonValueKind.Object)
        {
            if (paper.TryGetProperty("usd", out var usdNode) && TryParseDecimal(usdNode, out usd)) return true;
            if (paper.TryGetProperty("normal", out var normalNode) && TryParseDecimal(normalNode, out usd)) return true;
        }

        if (node.TryGetProperty("usd", out var directUsd) && TryParseDecimal(directUsd, out usd)) return true;
        if (node.TryGetProperty("normal", out var directNormal) && TryParseDecimal(directNormal, out usd)) return true;

        return false;
    }

    private static bool TryParseDecimal(JsonElement node, out decimal value)
    {
        value = 0m;
        return node.ValueKind switch
        {
            JsonValueKind.Number => node.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(node.GetString(), out value),
            _ => false
        };
    }
}
