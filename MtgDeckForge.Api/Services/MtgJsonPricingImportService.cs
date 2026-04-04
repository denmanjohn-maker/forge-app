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
            _logger.LogInformation("Starting pricing import — streaming printings...");

            // Stream-parse printings to build UUID→Name map
            var uuidToName = await StreamParseUuidToNameAsync(_settings.PrintingsUrl, cancellationToken);
            _logger.LogInformation("Parsed {Count} card names from printings", uuidToName.Count);

            // Stream-parse prices to build UUID→USD map
            var uuidToUsd = await StreamParseUuidToUsdAsync(_settings.PricesUrl, cancellationToken);
            _logger.LogInformation("Parsed {Count} prices", uuidToUsd.Count);

            // Build a name→price map (smaller than keeping both UUID maps)
            var nameToPriceMap = new Dictionary<string, (string name, string uuid, decimal usd)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (uuid, usd) in uuidToUsd)
            {
                if (!uuidToName.TryGetValue(uuid, out var name)) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var normalized = PricingService.NormalizeCardName(name);
                if (!nameToPriceMap.ContainsKey(normalized))
                    nameToPriceMap[normalized] = (name, uuid, usd);
            }

            // Free the large dictionaries
            uuidToName.Clear();
            uuidToUsd.Clear();

            _logger.LogInformation("Matched {Count} unique card prices", nameToPriceMap.Count);

            // Process in batches to avoid tracking too many entities
            var imported = 0;
            var now = DateTime.UtcNow;
            var allNames = nameToPriceMap.Keys.ToList();
            const int batchSize = 1000;

            for (var i = 0; i < allNames.Count; i += batchSize)
            {
                var batchNames = allNames.Skip(i).Take(batchSize).ToList();
                var existingByName = await _db.CardPrices
                    .Where(x => batchNames.Contains(x.NormalizedCardName))
                    .ToDictionaryAsync(x => x.NormalizedCardName, cancellationToken);

                foreach (var normalizedName in batchNames)
                {
                    var (name, uuid, usd) = nameToPriceMap[normalizedName];
                    existingByName.TryGetValue(normalizedName, out var existing);
                    if (existing is null)
                    {
                        _db.CardPrices.Add(new CardPrice
                        {
                            CardName = name,
                            NormalizedCardName = normalizedName,
                            SourceUuid = uuid,
                            PriceUsd = usd,
                            UpdatedAtUtc = now
                        });
                        imported++;
                    }
                    else
                    {
                        var changed = false;
                        if (!string.Equals(existing.CardName, name, StringComparison.Ordinal))
                        { existing.CardName = name; changed = true; }
                        if (!string.Equals(existing.SourceUuid, uuid, StringComparison.Ordinal))
                        { existing.SourceUuid = uuid; changed = true; }
                        if (existing.PriceUsd != usd)
                        { existing.PriceUsd = usd; changed = true; }
                        if (changed)
                        { existing.UpdatedAtUtc = now; imported++; }
                    }
                }

                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            // Re-attach and update the run record
            _db.PricingImportRuns.Attach(run);
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
            try
            {
                _db.ChangeTracker.Clear();
                _db.PricingImportRuns.Attach(run);
                run.Success = false;
                run.ImportedCount = 0;
                run.CompletedAtUtc = DateTime.UtcNow;
                run.Message = ex.Message;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch { /* best effort */ }
            return (false, 0, ex.Message);
        }
    }

    private async Task<Dictionary<string, string>> StreamParseUuidToNameAsync(string url, CancellationToken ct)
    {
        var dict = new Dictionary<string, string>(200_000, StringComparer.OrdinalIgnoreCase);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

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

    private async Task<Dictionary<string, decimal>> StreamParseUuidToUsdAsync(string url, CancellationToken ct)
    {
        var dict = new Dictionary<string, decimal>(200_000, StringComparer.OrdinalIgnoreCase);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

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
