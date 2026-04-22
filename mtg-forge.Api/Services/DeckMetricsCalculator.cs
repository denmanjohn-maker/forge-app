using System.Text.RegularExpressions;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public record DeckMetrics(
    Dictionary<int, int>    ManaCurve,          // CMC bucket → card count (non-land; 7+ grouped)
    double                  AverageCmc,
    int                     LandCount,
    int                     CreatureCount,
    int                     RampCount,
    int                     RemovalCount,
    int                     CardDrawCount,
    Dictionary<string, int> ColorPipDistribution, // W/U/B/R/G → pip count across all mana costs
    decimal                 TotalCost
);

public static class DeckMetricsCalculator
{
    private static readonly Regex PipRegex = new(@"\{([WUBRG])\}", RegexOptions.Compiled);

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
        var totalSpells   = nonLands.Sum(c => c.Quantity);
        var weightedCmcSum = nonLands.Sum(c => (double)c.Cmc * c.Quantity);
        var avgCmc = totalSpells > 0 ? weightedCmcSum / totalSpells : 0.0;

        // Category counts
        int Count(string keyword) =>
            cardList
                .Where(c => c.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Quantity);

        var landCount     = cardList.Where(c => c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Quantity);
        var creatureCount = cardList.Where(c => c.CardType.Contains("Creature", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Quantity);
        var rampCount     = Count("Ramp");
        var removalCount  = Count("Removal");
        var drawCount     = Count("Draw") + Count("Card Draw");

        // Color pip distribution (deduplicate multi-category cards counted twice by "Draw"+"Card Draw")
        var pipDist = new Dictionary<string, int> { ["W"] = 0, ["U"] = 0, ["B"] = 0, ["R"] = 0, ["G"] = 0 };
        foreach (var c in cardList)
        {
            if (string.IsNullOrEmpty(c.ManaCost))
                continue;
            foreach (Match m in PipRegex.Matches(c.ManaCost))
            {
                var pip = m.Groups[1].Value;
                if (pipDist.ContainsKey(pip))
                    pipDist[pip] += c.Quantity;
            }
        }

        var totalCost = cardList.Sum(c => c.EstimatedPrice * c.Quantity);

        return new DeckMetrics(
            ManaCurve:           curve,
            AverageCmc:          Math.Round(avgCmc, 2),
            LandCount:           landCount,
            CreatureCount:       creatureCount,
            RampCount:           rampCount,
            RemovalCount:        removalCount,
            CardDrawCount:       drawCount,
            ColorPipDistribution: pipDist,
            TotalCost:           totalCost
        );
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
