namespace Biktal.WebMVC.Models;

public sealed class InventoryOverviewViewModel
{
    public int SkuCount { get; init; }

    public int WarehouseCount { get; init; }

    public int OpenPurchaseOrderCount { get; init; }

    public int CategoryCount { get; init; }

    public int BrandCount { get; init; }

    public int SupplierCount { get; init; }

    public int LowStockSkuCount { get; init; }

    public int OkStockSkuCount { get; init; }

    public int OutOfStockSkuCount { get; init; }

    public int LowStockThreshold { get; init; }

    public int TotalStockUnits { get; init; }

    public decimal InventoryRetailValue { get; init; }

    public int MovementsThisMonth { get; init; }

    public string StockHealthLabel =>
        SkuCount == 0
            ? "No products"
            : LowStockSkuCount == 0 && OutOfStockSkuCount == 0
                ? "All OK"
                : $"{LowStockSkuCount + OutOfStockSkuCount} need attention";
}
