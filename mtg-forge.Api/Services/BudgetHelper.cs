namespace MtgForge.Api.Services;

/// <summary>
/// Shared budget-range helpers used across services and controllers.
/// </summary>
public static class BudgetHelper
{
    /// <summary>
    /// Returns the maximum total deck price in dollars for a given budget range string,
    /// or <c>null</c> when there is no limit.
    /// </summary>
    public static decimal? GetBudgetMax(string budgetRange)
    {
        if (budgetRange.Contains("under $50", StringComparison.OrdinalIgnoreCase)
            || budgetRange.Equals("Budget", StringComparison.OrdinalIgnoreCase))
            return 50m;

        if (budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$500", StringComparison.OrdinalIgnoreCase))
            return 500m;

        if (budgetRange.Contains("$50", StringComparison.OrdinalIgnoreCase)
            && budgetRange.Contains("$150", StringComparison.OrdinalIgnoreCase))
            return 150m;

        return null; // no limit
    }
}
