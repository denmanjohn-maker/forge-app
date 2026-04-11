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

    public static string NormalizeCardName(string name) => name.Trim().ToLowerInvariant();

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
            .OrderBy(x => Guid.NewGuid()) // random sample
            .Take(count)
            .Select(x => new ValueTuple<string, decimal>(x.CardName, x.PriceUsd))
            .ToListAsync();
    }
}
