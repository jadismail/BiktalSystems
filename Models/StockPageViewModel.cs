namespace Biktal.WebMVC.Models;

public sealed class StockPageViewModel
{
    /// <summary>SKUs with quantity in (0, threshold] count as low stock (not out).</summary>
    public int LowStockThreshold { get; init; } = 5;

    public IReadOnlyList<StockRowViewModel> Rows { get; init; } = Array.Empty<StockRowViewModel>();

    public int TotalSkus => Rows.Count;

    public long TotalUnitsOnHand => Rows.Sum(r => (long)r.StockQuantity);

    public int OutOfStockCount => Rows.Count(r => r.StockQuantity <= 0);

    /// <summary>On hand &gt; 0 and &lt;= <see cref="LowStockThreshold"/>.</summary>
    public int LowStockCount => Rows.Count(r => r.StockQuantity > 0 && r.StockQuantity <= LowStockThreshold);
}
