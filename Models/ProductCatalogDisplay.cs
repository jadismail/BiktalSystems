namespace Biktal.WebMVC.Models;

/// <summary>Display helpers for catalog rows (margin on cost = (price − cost) ÷ cost × 100).</summary>
public static class ProductCatalogDisplay
{
    /// <summary>Returns markup on cost as a percentage, or null when cost is zero (undefined).</summary>
    public static decimal? MarkupOnCostPercent(decimal price, decimal cost)
    {
        if (cost <= 0m)
            return null;

        return (price - cost) / cost * 100m;
    }
}
