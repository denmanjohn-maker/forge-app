using System.Text.RegularExpressions;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public record DeckMetrics(
    Dictionary<int, int>    ManaCurve,            // CMC bucket → card count (non-land; 7+ grouped)
    double                  AverageCmc,
    int                     LandCount,
    int                     CreatureCount,
    int?                    RampCount,            // null when categories are non-semantic (imported decks)
    int?                    RemovalCount,
    int?                    CardDrawCount,
    Dictionary<string, int> ColorPipDistribution, // W/U/B/R/G → pip count across all mana costs
    decimal                 TotalCost
);

public static class DeckMetricsCalculator
{
    // Matches any {…} mana symbol so we can extract color letters from hybrid/phyrexian too
    private static readonly Regex ManaCostSymbolRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    // Functional semantic categories produced by the AI deck generator.
    // Structural categories ("Commander", "Mainboard", "Sideboard") are intentionally
    // excluded so that imported CSV decks with only a commander row still return null counts.
    private static readonly HashSet<string> SemanticCategories = new(
        ["Ramp", "Removal", "Board Wipes", "Card Draw", "Draw",
         "Win Conditions", "Synergy Pieces", "Utility"],
        StringComparer.OrdinalIgnoreCase);

    public static DeckMetrics Calculate(IEnumerable<CardEntry> cards)
    {
        var cardList = cards.ToList();

        var nonLands = cardList
            .Where(c => !c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Mana curve: expand by quantity, cap at 7+
        var curve = new Dictionary<int, int>();
        foreach (var c in nonLands)
        {
            var bucket = Math.Min(c.Cmc, 7);
            curve[bucket] = curve.GetValueOrDefault(bucket) + c.Quantity;
        }

        // Average CMC weighted by quantity
        var totalSpells    = nonLands.Sum(c => c.Quantity);
        var weightedCmcSum = nonLands.Sum(c => (double)c.Cmc * c.Quantity);
        var avgCmc         = totalSpells > 0 ? weightedCmcSum / totalSpells : 0.0;

        var landCount     = cardList.Where(c => c.CardType.Contains("Land",     StringComparison.OrdinalIgnoreCase)).Sum(c => c.Quantity);
        var creatureCount = cardList.Where(c => c.CardType.Contains("Creature", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Quantity);

        // Category counts — only meaningful when the deck uses AI-assigned semantic categories.
        // Imported CSVs use "Mainboard"/"Sideboard" etc., which would produce 0s and mislead analysis.
        var hasSemanticCategories = cardList.Any(c => SemanticCategories.Contains(c.Category));

        int? rampCount    = null;
        int? removalCount = null;
        int? drawCount    = null;

        if (hasSemanticCategories)
        {
            // Use exact-match tokenization to avoid "Draw" Contains-matching "Card Draw"
            rampCount    = CountByExactCategory(cardList, "Ramp");
            removalCount = CountByExactCategory(cardList, "Removal");
            drawCount    = CountByExactCategory(cardList, "Draw", "Card Draw");
        }

        // Color pip distribution across all mana costs, weighted by card quantity
        var pipDist = new Dictionary<string, int> { ["W"] = 0, ["U"] = 0, ["B"] = 0, ["R"] = 0, ["G"] = 0 };
        foreach (var c in cardList)
        {
            if (string.IsNullOrEmpty(c.ManaCost))
                continue;
            foreach (Match m in ManaCostSymbolRegex.Matches(c.ManaCost))
            {
                // Count each color pip appearing in the symbol content.
                // Handles singles {G}, hybrids {W/U}, phyrexian {G/P}, and generic-hybrid {2/R}.
                var symbol = m.Groups[1].Value;
                foreach (var ch in symbol)
                {
                    var key = ch.ToString();
                    if (pipDist.ContainsKey(key))
                        pipDist[key] += c.Quantity;
                }
            }
        }

        var totalCost = cardList.Sum(c => c.EstimatedPrice * c.Quantity);

        return new DeckMetrics(
            ManaCurve:            curve,
            AverageCmc:           Math.Round(avgCmc, 2),
            LandCount:            landCount,
            CreatureCount:        creatureCount,
            RampCount:            rampCount,
            RemovalCount:         removalCount,
            CardDrawCount:        drawCount,
            ColorPipDistribution: pipDist,
            TotalCost:            totalCost
        );
    }

    private static int CountByExactCategory(List<CardEntry> cards, params string[] categories)
    {
        var set = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        return cards
            .Where(c => CategoryTokens(c.Category).Any(t => set.Contains(t)))
            .Sum(c => c.Quantity);
    }

    // Splits a category string on common delimiters so "Ramp / Utility" → ["Ramp", "Utility"]
    private static IEnumerable<string> CategoryTokens(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            yield break;
        foreach (var token in category.Split([',', ';', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!string.IsNullOrWhiteSpace(token))
                yield return token;
    }

    public static string FormatManaCurve(Dictionary<int, int> curve)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i <= 7; i++)
        {
            var count = curve.GetValueOrDefault(i);
            if (count == 0)
                continue;
            var label = i == 7 ? "7+" : i.ToString();
            sb.Append($"CMC {label}: {count}  ");
        }
        return sb.ToString().Trim();
    }

    public static string FormatPips(Dictionary<string, int> pips)
    {
        var parts = pips
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}:{kv.Value}");
        return string.Join("  ", parts);
    }
}
