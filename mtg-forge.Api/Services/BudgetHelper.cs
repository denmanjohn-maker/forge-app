namespace MtgForge.Api.Services;

public enum BudgetTier
{
    Budget,
    MidRange,
    High,
    Unlimited
}

/// <summary>
/// Shared budget-range helpers used across services and controllers.
/// </summary>
public static class BudgetHelper
{
    /// <summary>
    /// Parses a budget range string into a <see cref="BudgetTier"/> enum.
    /// </summary>
    public static BudgetTier ParseBudgetTier(string budgetRange)
    {
        if (string.IsNullOrWhiteSpace(budgetRange))
            return BudgetTier.Unlimited;

        var lowered = budgetRange.ToLowerInvariant();

        if (lowered.Contains("under $50") || lowered.Equals("budget"))
            return BudgetTier.Budget;

        if (lowered.Contains("$150") && lowered.Contains("$500"))
            return BudgetTier.High;

        if (lowered.Contains("$50") && lowered.Contains("$150"))
            return BudgetTier.MidRange;

        return BudgetTier.Unlimited;
    }

    /// <summary>
    /// Returns the maximum total deck price in dollars for a given <see cref="BudgetTier"/>,
    /// or <c>null</c> when there is no limit.
    /// </summary>
    public static decimal? GetBudgetMax(BudgetTier tier) => tier switch
    {
        BudgetTier.Budget => 50m,
        BudgetTier.MidRange => 150m,
        BudgetTier.High => 500m,
        BudgetTier.Unlimited => null,
        _ => null
    };

    /// <summary>
    /// Returns the maximum total deck price in dollars for a given budget range string,
    /// or <c>null</c> when there is no limit.
    /// </summary>
    public static decimal? GetBudgetMax(string budgetRange) =>
        GetBudgetMax(ParseBudgetTier(budgetRange));
}
