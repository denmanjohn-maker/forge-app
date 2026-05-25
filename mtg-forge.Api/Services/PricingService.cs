using Microsoft.EntityFrameworkCore;
using MtgForge.Api.Data;

namespace MtgForge.Api.Services;

/// <summary>
/// Provides card-price lookups backed by the PostgreSQL pricing cache, which is
/// populated daily by <see cref="MtgJsonPricingImportService"/>.
/// <para>
/// All name comparisons use <see cref="NormalizeCardName"/> so that differences in
/// smart quotes, punctuation, and casing don't cause lookup misses.
/// </para>
/// </summary>
public class PricingService
{
    private readonly AppDbContext _db;

    public PricingService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Normalizes a card name for consistent price lookups: lowercases, replaces smart
    /// quotes, strips punctuation (except apostrophes and hyphens), and collapses
    /// multiple spaces. Must be applied to both the storage key and the lookup key.
    /// </summary>
    public static string NormalizeCardName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var normalized = name.Trim().ToLowerInvariant();

        // Replace smart quotes with regular quotes
        normalized = normalized.Replace('\u2018', '\'').Replace('\u2019', '\'');
        normalized = normalized.Replace('\u201c', '"').Replace('\u201d', '"');

        // Remove punctuation except apostrophes, hyphens, and whitespace
        normalized = new string(normalized
            .Where(c => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || char.IsWhiteSpace(c))
            .ToArray());

        // Collapse multiple spaces into one
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        return normalized;
    }

    /// <summary>
    /// Returns the USD price for a single card, or <c>null</c> if the card is not
    /// in the pricing cache.
    /// </summary>
    public async Task<decimal?> GetCardPriceAsync(string cardName)
    {
        var normalized = NormalizeCardName(cardName);
        var row = await _db.CardPrices.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedCardName == normalized);
        return row?.PriceUsd;
    }

    /// <summary>
    /// Looks up prices for a batch of cards in a single database query and writes the
    /// results back to each <see cref="CardEntry.EstimatedPrice"/>. Cards not found in
    /// the cache are left unchanged.
    /// </summary>
    public async Task ApplyPricesAsync(List<Models.CardEntry> cards)
    {
        if (cards.Count == 0) return;

        var names = cards.Select(c => NormalizeCardName(c.Name)).Distinct().ToList();
        var map = await _db.CardPrices.AsNoTracking()
            .Where(x => names.Contains(x.NormalizedCardName))
            .ToDictionaryAsync(x => x.NormalizedCardName, x => x.PriceUsd);

        foreach (var card in cards)
        {
            var key = NormalizeCardName(card.Name);
            if (map.TryGetValue(key, out var p))
            {
                card.EstimatedPrice = p;
            }
        }
    }

    /// <summary>
    /// Returns a random sample of card names with verified prices at or below the given max price.
    /// </summary>
    public async Task<List<(string CardName, decimal Price)>> GetCheapCardsAsync(decimal maxPrice, int count = 200)
    {
        return await _db.CardPrices.AsNoTracking()
            .Where(x => x.PriceUsd > 0 && x.PriceUsd <= maxPrice)
            .OrderBy(x => EF.Functions.Random())
            .Take(count)
            .Select(x => new ValueTuple<string, decimal>(x.CardName, x.PriceUsd))
            .ToListAsync();
    }
}
