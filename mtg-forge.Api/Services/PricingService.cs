using Microsoft.EntityFrameworkCore;
using MtgForge.Api.Data;

namespace MtgForge.Api.Services;

public class PricingService
{
    private readonly AppDbContext _db;

    public PricingService(AppDbContext db)
    {
        _db = db;
    }

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

    public async Task<decimal?> GetCardPriceAsync(string cardName)
    {
        var normalized = NormalizeCardName(cardName);
        var row = await _db.CardPrices.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedCardName == normalized);
        return row?.PriceUsd;
    }

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
