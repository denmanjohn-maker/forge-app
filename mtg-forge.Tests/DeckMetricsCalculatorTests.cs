using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Tests;

public class DeckMetricsCalculatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CardEntry Card(
        string name,
        int quantity    = 1,
        int cmc         = 0,
        string type     = "Instant",
        string category = "Removal",
        string manaCost = "",
        decimal price   = 0.50m) =>
        new()
        {
            Name           = name,
            Quantity       = quantity,
            Cmc            = cmc,
            CardType       = type,
            Category       = category,
            ManaCost       = manaCost,
            EstimatedPrice = price,
            RoleInDeck     = ""
        };

    // ── Mana Curve ───────────────────────────────────────────────────────────

    [Fact]
    public void ManaCurve_BucketsNonLandCards_ByQuantity()
    {
        var cards = new[]
        {
            Card("A", cmc: 2, quantity: 2),
            Card("B", cmc: 4, quantity: 1),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(2, m.ManaCurve[2]);
        Assert.Equal(1, m.ManaCurve[4]);
        Assert.False(m.ManaCurve.ContainsKey(0));
    }

    [Fact]
    public void ManaCurve_Excludes_LandCards()
    {
        var cards = new[]
        {
            Card("Forest", cmc: 0, type: "Basic Land", category: "Land"),
            Card("Shock",  cmc: 1),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1, m.ManaCurve[1]);
        Assert.False(m.ManaCurve.ContainsKey(0));
    }

    [Fact]
    public void ManaCurve_CapsAt7Plus()
    {
        var cards = new[]
        {
            Card("Eldrazi", cmc: 10),
            Card("Titan",   cmc: 7),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(2, m.ManaCurve[7]);
        Assert.False(m.ManaCurve.ContainsKey(10));
    }

    // ── Average CMC ──────────────────────────────────────────────────────────

    [Fact]
    public void AverageCmc_IsWeightedByQuantity()
    {
        var cards = new[]
        {
            Card("A", cmc: 2, quantity: 4),  // 2 × 4 = 8
            Card("B", cmc: 4, quantity: 1),  // 4 × 1 = 4  → total=12 / 5 = 2.4
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(2.4, m.AverageCmc);
    }

    [Fact]
    public void AverageCmc_IgnoresLands()
    {
        var cards = new[]
        {
            Card("Forest", cmc: 0, type: "Basic Land", category: "Land"),
            Card("Shock",  cmc: 1),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1.0, m.AverageCmc);
    }

    // ── Category Counts ──────────────────────────────────────────────────────

    [Fact]
    public void CategoryCounts_AreNull_ForImportedDecks()
    {
        var cards = new[]
        {
            Card("Lightning Bolt", category: "Mainboard"),
            Card("Counterspell",   category: "Sideboard"),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Null(m.RampCount);
        Assert.Null(m.RemovalCount);
        Assert.Null(m.CardDrawCount);
    }

    [Fact]
    public void CategoryCounts_AreComputed_WhenSemanticCategoriesPresent()
    {
        var cards = new[]
        {
            Card("Sol Ring",         category: "Ramp",     cmc: 1),
            Card("Lightning Bolt",   category: "Removal",  cmc: 1),
            Card("Brainstorm",       category: "Card Draw", cmc: 1),
            Card("Ponder",           category: "Draw",      cmc: 1),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1, m.RampCount);
        Assert.Equal(1, m.RemovalCount);
        // "Card Draw" and "Draw" — both counted, no double-count
        Assert.Equal(2, m.CardDrawCount);
    }

    [Fact]
    public void DrawCount_DoesNotDoubleCount_CardDrawCategory()
    {
        // A section named "Card Draw" should count once, not twice
        var cards = new[]
        {
            Card("Brainstorm", category: "Card Draw"),
            Card("Ponder",     category: "Card Draw"),
            Card("Preordain",  category: "Draw"),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(3, m.CardDrawCount);
    }

    // ── Color Pip Distribution ───────────────────────────────────────────────

    [Fact]
    public void PipDistribution_CountsSingleColorPips()
    {
        var cards = new[]
        {
            Card("Forest",       manaCost: ""),
            Card("Llanowar Elf", manaCost: "{G}",    quantity: 1),
            Card("Shock",        manaCost: "{R}",    quantity: 2),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1, m.ColorPipDistribution["G"]);
        Assert.Equal(2, m.ColorPipDistribution["R"]);
        Assert.Equal(0, m.ColorPipDistribution["W"]);
    }

    [Fact]
    public void PipDistribution_CountsHybridManaSymbols()
    {
        // {W/U} should contribute 1 pip to both W and U
        var cards = new[] { Card("Agony Warp", manaCost: "{W/U}{B}") };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1, m.ColorPipDistribution["W"]);
        Assert.Equal(1, m.ColorPipDistribution["U"]);
        Assert.Equal(1, m.ColorPipDistribution["B"]);
    }

    [Fact]
    public void PipDistribution_CountsPhyrexianMana()
    {
        // {G/P} should contribute 1 pip to G (P is not a color)
        var cards = new[] { Card("Birthing Pod", manaCost: "{3}{G/P}") };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(1, m.ColorPipDistribution["G"]);
        Assert.Equal(0, m.ColorPipDistribution["W"]);
    }

    [Fact]
    public void PipDistribution_CountsGenericHybridMana()
    {
        // {2/R} should contribute 1 pip to R
        var cards = new[] { Card("Flame Javelin", manaCost: "{2/R}{2/R}{2/R}") };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(3, m.ColorPipDistribution["R"]);
    }

    [Fact]
    public void PipDistribution_IsWeightedByQuantity()
    {
        var cards = new[] { Card("Counterspell", manaCost: "{U}{U}", quantity: 3) };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(6, m.ColorPipDistribution["U"]);
    }

    // ── Total Cost ────────────────────────────────────────────────────────────

    [Fact]
    public void TotalCost_IsWeightedByQuantity()
    {
        var cards = new[]
        {
            Card("A", quantity: 4, price: 1.00m),
            Card("B", quantity: 2, price: 2.50m),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(9.00m, m.TotalCost);
    }

    // ── Land / Creature Count ─────────────────────────────────────────────────

    [Fact]
    public void LandCount_SumsLandCardQuantities()
    {
        var cards = new[]
        {
            Card("Island",  type: "Basic Land", category: "Land", quantity: 10),
            Card("Shock",   type: "Instant"),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(10, m.LandCount);
        Assert.Equal(0,  m.CreatureCount);
    }

    [Fact]
    public void CreatureCount_SumsCreatureCardQuantities()
    {
        var cards = new[]
        {
            Card("Llanowar Elves", type: "Creature — Elf Druid", quantity: 4),
            Card("Counterspell",   type: "Instant"),
        };
        var m = DeckMetricsCalculator.Calculate(cards);

        Assert.Equal(4, m.CreatureCount);
    }

    // ── FormatManaCurve ──────────────────────────────────────────────────────

    [Fact]
    public void FormatManaCurve_SkipsEmptyBuckets_And_Labels7Plus()
    {
        var curve = new Dictionary<int, int> { [1] = 5, [7] = 3 };
        var result = DeckMetricsCalculator.FormatManaCurve(curve);

        Assert.Contains("CMC 1: 5", result);
        Assert.Contains("CMC 7+: 3", result);
        Assert.DoesNotContain("CMC 0", result);
        Assert.DoesNotContain("CMC 2", result);
    }
}
