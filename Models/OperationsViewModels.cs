namespace Biktal.WebMVC.Models;

public sealed class OperationsHubViewModel
{
    public string PeriodLabel { get; init; } = "Month to date";

    public decimal DailySalesToday { get; init; }

    public int DailySaleCountToday { get; init; }

    public decimal MonthlySalesMtd { get; init; }

    public int MonthlySaleCountMtd { get; init; }

    public int LowStockSkuCount { get; init; }

    public int OpenPurchaseOrderCount { get; init; }

    public int InventoryMovementCountMtd { get; init; }
}

public sealed class OperationsDailySalesViewModel
{
    public DateOnly Date { get; init; }

    public decimal PosRevenue { get; init; }

    public int PosSaleCount { get; init; }

    public decimal RepairRevenue { get; init; }

    public int RepairCount { get; init; }

    public decimal TotalRevenue => PosRevenue + RepairRevenue;
}

public sealed class OperationsMonthlySalesViewModel
{
    public string PeriodLabel { get; init; } = string.Empty;

    public decimal PosRevenue { get; init; }

    public int PosSaleCount { get; init; }

    public decimal RepairRevenue { get; init; }

    public int RepairCount { get; init; }

    public decimal TotalRevenue => PosRevenue + RepairRevenue;
}

public sealed class OperationsPurchaseOrdersViewModel
{
    public int OpenCount { get; init; }

    public int DraftCount { get; init; }

    public int ReceivedMtdCount { get; init; }

    public decimal OpenOrderValue { get; init; }

    public IReadOnlyList<OperationsPurchaseOrderRowViewModel> RecentOrders { get; init; } =
        Array.Empty<OperationsPurchaseOrderRowViewModel>();
}

public sealed class OperationsPurchaseOrderRowViewModel
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public DateOnly OrderDate { get; init; }
}

public sealed class OperationsComingSoonViewModel
{
    public required string FeatureTitle { get; init; }

    public string FeatureDescription { get; init; } = string.Empty;
}
