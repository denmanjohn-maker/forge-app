using System.Buffers;
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
            _logger.LogInformation("Starting pricing import — streaming with Utf8JsonReader...");

            var uuidToName = await StreamParseUuidToNameAsync(_settings.PrintingsUrl, cancellationToken);
            _logger.LogInformation("Parsed {Count} card names from printings", uuidToName.Count);

            var uuidToUsd = await StreamParseUuidToUsdAsync(_settings.PricesUrl, cancellationToken);
            _logger.LogInformation("Parsed {Count} prices", uuidToUsd.Count);

            var nameToPriceMap = new Dictionary<string, (string name, string uuid, decimal usd)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (uuid, usd) in uuidToUsd)
            {
                if (!uuidToName.TryGetValue(uuid, out var name)) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var normalized = PricingService.NormalizeCardName(name);
                if (!nameToPriceMap.ContainsKey(normalized))
                    nameToPriceMap[normalized] = (name, uuid, usd);
            }

            uuidToName.Clear();
            uuidToUsd.Clear();
            _logger.LogInformation("Matched {Count} unique card prices", nameToPriceMap.Count);

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
                _logger.LogInformation("Saved batch {Batch}/{Total}",
                    i / batchSize + 1, (allNames.Count + batchSize - 1) / batchSize);
            }

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

    #region Streaming AllPrintings parser

    // Mutable state for the AllPrintings parser, passed between sync chunk-processing calls
    private class PrintingsParserState
    {
        public JsonReaderState ReaderState;
        public bool InData;
        public bool InSet;
        public bool InCardsArray;
        public bool InCard;
        public bool AwaitingCardsValue;
        public string? PendingProp;
        public string? CurUuid;
        public string? CurName;
        public int SetsProcessed;
    }

    private async Task<Dictionary<string, string>> StreamParseUuidToNameAsync(string url, CancellationToken ct)
    {
        var dict = new Dictionary<string, string>(200_000, StringComparer.OrdinalIgnoreCase);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        var state = new PrintingsParserState();
        try
        {
            int leftover = 0;
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(leftover, buffer.Length - leftover), ct);
                bool isFinal = bytesRead == 0;
                int total = leftover + bytesRead;
                if (total == 0) break;

                leftover = ProcessPrintingsChunk(buffer.AsSpan(0, total), isFinal, state, dict);

                if (leftover > 0)
                    Buffer.BlockCopy(buffer, total - leftover, buffer, 0, leftover);

                if (isFinal) break;

                if (leftover >= buffer.Length - 4096)
                {
                    var newBuf = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, newBuf, 0, leftover);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuf;
                    _logger.LogWarning("Grew printings buffer to {Size}MB", buffer.Length / 1024 / 1024);
                }
            }

            _logger.LogInformation("Printings parsing complete: {Sets} sets, {Cards} UUIDs", state.SetsProcessed, dict.Count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return dict;
    }

    // Synchronous chunk processor — Utf8JsonReader is a ref struct, not allowed in async methods (C# 12)
    private static int ProcessPrintingsChunk(ReadOnlySpan<byte> data, bool isFinal, PrintingsParserState s, Dictionary<string, string> dict)
    {
        var reader = new Utf8JsonReader(data, isFinal, s.ReaderState);

        while (reader.Read())
        {
            var depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (!s.InData && depth == 1 && reader.ValueTextEquals("data"u8))
                        s.InData = true;
                    else if (s.InSet && !s.InCardsArray && depth == 3)
                        s.AwaitingCardsValue = reader.ValueTextEquals("cards"u8);
                    else if (s.InCard && depth == 5)
                    {
                        if (reader.ValueTextEquals("uuid"u8)) s.PendingProp = "uuid";
                        else if (reader.ValueTextEquals("name"u8)) s.PendingProp = "name";
                        else s.PendingProp = null;
                    }
                    break;

                case JsonTokenType.String:
                    if (s.PendingProp != null && s.InCard && depth == 5)
                    {
                        var val = reader.GetString();
                        if (s.PendingProp == "uuid") s.CurUuid = val;
                        else s.CurName = val;
                        s.PendingProp = null;
                    }
                    break;

                case JsonTokenType.StartObject:
                    if (s.InData && !s.InSet && depth == 2)
                        s.InSet = true;
                    else if (s.InCardsArray && !s.InCard && depth == 4)
                    {
                        s.InCard = true;
                        s.CurUuid = null;
                        s.CurName = null;
                        s.PendingProp = null;
                    }
                    break;

                case JsonTokenType.StartArray:
                    if (s.AwaitingCardsValue && depth == 3)
                        s.InCardsArray = true;
                    s.AwaitingCardsValue = false;
                    break;

                case JsonTokenType.EndObject:
                    if (s.InCard && depth == 4)
                    {
                        if (!string.IsNullOrEmpty(s.CurUuid) && !string.IsNullOrEmpty(s.CurName))
                            dict[s.CurUuid] = s.CurName;
                        s.InCard = false;
                    }
                    else if (s.InSet && depth == 2)
                    {
                        s.InSet = false;
                        s.InCardsArray = false;
                        s.SetsProcessed++;
                    }
                    else if (s.InData && depth == 1)
                        s.InData = false;
                    break;

                case JsonTokenType.EndArray:
                    if (s.InCardsArray && depth == 3)
                        s.InCardsArray = false;
                    break;
            }
        }

        s.ReaderState = reader.CurrentState;
        return (int)(data.Length - reader.BytesConsumed);
    }

    #endregion

    #region Streaming AllPricesToday parser

    private class PricesParserState
    {
        public JsonReaderState ReaderState;
        public bool InData;
        public string? CurrentUuid;
        public decimal BestPrice;
        public bool HasPrice;
    }

    private async Task<Dictionary<string, decimal>> StreamParseUuidToUsdAsync(string url, CancellationToken ct)
    {
        var dict = new Dictionary<string, decimal>(200_000, StringComparer.OrdinalIgnoreCase);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        var state = new PricesParserState();
        try
        {
            int leftover = 0;
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(leftover, buffer.Length - leftover), ct);
                bool isFinal = bytesRead == 0;
                int total = leftover + bytesRead;
                if (total == 0) break;

                leftover = ProcessPricesChunk(buffer.AsSpan(0, total), isFinal, state, dict);

                if (leftover > 0)
                    Buffer.BlockCopy(buffer, total - leftover, buffer, 0, leftover);

                if (isFinal) break;

                if (leftover >= buffer.Length - 4096)
                {
                    var newBuf = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, newBuf, 0, leftover);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuf;
                }
            }

            _logger.LogInformation("Price parsing complete: {Count} UUIDs with prices", dict.Count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return dict;
    }

    // In AllPricesToday, every number inside a UUID object is a price.
    // Take the first positive one found (typically retail normal).
    private static int ProcessPricesChunk(ReadOnlySpan<byte> data, bool isFinal, PricesParserState s, Dictionary<string, decimal> dict)
    {
        var reader = new Utf8JsonReader(data, isFinal, s.ReaderState);

        while (reader.Read())
        {
            var depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (!s.InData && depth == 1 && reader.ValueTextEquals("data"u8))
                        s.InData = true;
                    else if (s.InData && depth == 2 && s.CurrentUuid == null)
                    {
                        s.CurrentUuid = reader.GetString();
                        s.BestPrice = 0;
                        s.HasPrice = false;
                    }
                    break;

                case JsonTokenType.Number:
                    if (s.CurrentUuid != null && !s.HasPrice)
                    {
                        if (reader.TryGetDecimal(out var price) && price > 0)
                        {
                            s.BestPrice = price;
                            s.HasPrice = true;
                        }
                    }
                    break;

                case JsonTokenType.String:
                    if (s.CurrentUuid != null && !s.HasPrice && depth > 2)
                    {
                        var str = reader.GetString();
                        if (str != null && decimal.TryParse(str, out var price) && price > 0)
                        {
                            s.BestPrice = price;
                            s.HasPrice = true;
                        }
                    }
                    break;

                case JsonTokenType.EndObject:
                    if (s.CurrentUuid != null && depth == 2)
                    {
                        if (s.HasPrice)
                            dict[s.CurrentUuid] = s.BestPrice;
                        s.CurrentUuid = null;
                    }
                    else if (s.InData && depth == 1)
                        s.InData = false;
                    break;
            }
        }

        s.ReaderState = reader.CurrentState;
        return (int)(data.Length - reader.BytesConsumed);
    }

    #endregion
}
