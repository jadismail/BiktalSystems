using Biktal.Domain.Finance;
using Biktal.Domain.Inventory;
using Biktal.Domain.Repairs;

namespace Biktal.WebMVC.Models;

public static class GlAccountTypeReportLabels
{
    public static string Title(GlAccountType t) => t switch
    {
        GlAccountType.Asset => "Asset",
        GlAccountType.Liability => "Liability",
        GlAccountType.Equity => "Equity",
        GlAccountType.Revenue => "Revenue",
        GlAccountType.Expense => "Expense",
        _ => t.ToString()
    };
}

public static class PurchaseOrderStatusReportLabels
{
    public static string Title(PurchaseOrderStatus s) => s switch
    {
        PurchaseOrderStatus.Draft => "Draft",
        PurchaseOrderStatus.Open => "Open",
        PurchaseOrderStatus.Received => "Received",
        PurchaseOrderStatus.Cancelled => "Cancelled",
        _ => s.ToString()
    };
}

public sealed class ReportsIndexViewModel
{
    public int CatalogSkuCount { get; init; }

    public int TotalStockUnits { get; init; }

    public decimal InventoryRetailValue { get; init; }

    public decimal InventoryCostValue { get; init; }

    public decimal InventoryUnrealizedMargin { get; init; }

    public int RepairTicketCount { get; init; }

    public int OpenRepairTicketCount { get; init; }

    public int OpenPurchaseOrderCount { get; init; }

    public int JournalEntryCount { get; init; }

    public decimal ExpensesThisMonth { get; init; }

    public int ActiveCrmCustomerCount { get; init; }

    public int OpenCrmSupportTicketCount { get; init; }
}

public sealed class ReportsSalesCatalogRowViewModel
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal LineRetailValue { get; init; }

    public decimal LineCostValue { get; init; }

    public decimal LineMarginValue { get; init; }
}

public sealed class ReportsSalesCategoryRowViewModel
{
    public string Category { get; init; } = string.Empty;

    public int SkuCount { get; init; }

    public int TotalUnits { get; init; }

    public decimal RetailValue { get; init; }
}

public sealed class ReportsSalesPageViewModel
{
    public IReadOnlyList<ReportsSalesCatalogRowViewModel> TopStockRetailRows { get; init; } =
        Array.Empty<ReportsSalesCatalogRowViewModel>();

    public IReadOnlyList<ReportsSalesCategoryRowViewModel> CategoryMix { get; init; } =
        Array.Empty<ReportsSalesCategoryRowViewModel>();
}

public sealed class ReportsInventoryLowStockRowViewModel
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public decimal LineRetailValue { get; init; }
}

public sealed class ReportsInventoryCategoryRowViewModel
{
    public string Category { get; init; } = string.Empty;

    public int SkuCount { get; init; }

    public int TotalUnits { get; init; }

    public decimal RetailValue { get; init; }

    public decimal CostValue { get; init; }
}

public sealed class ReportsPurchaseOrderRowViewModel
{
    public Guid Id { get; init; }

    public string PoNumber { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    public PurchaseOrderStatus Status { get; init; }

    public decimal LineTotal { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class ReportsInventoryPageViewModel
{
    public int CatalogSkuCount { get; init; }

    public int TotalStockUnits { get; init; }

    public decimal InventoryRetailValue { get; init; }

    public decimal InventoryCostValue { get; init; }

    public int LowStockSkuCount { get; init; }

    public int LowStockThreshold { get; init; } = 5;

    public IReadOnlyList<ReportsInventoryLowStockRowViewModel> LowStockRows { get; init; } =
        Array.Empty<ReportsInventoryLowStockRowViewModel>();

    public IReadOnlyList<ReportsInventoryCategoryRowViewModel> CategoryValuation { get; init; } =
        Array.Empty<ReportsInventoryCategoryRowViewModel>();

    public IReadOnlyList<ReportsPurchaseOrderRowViewModel> RecentPurchaseOrders { get; init; } =
        Array.Empty<ReportsPurchaseOrderRowViewModel>();
}

public sealed class ReportsRepairStatusCountViewModel
{
    public RepairTicketStatus Status { get; init; }

    public int Count { get; init; }
}

public sealed class ReportsRepairRecentRowViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string DeviceSummary { get; init; } = string.Empty;

    public RepairTicketStatus Status { get; init; }

    public decimal? EstimatedPrice { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class ReportsRepairsPageViewModel
{
    public int TotalTickets { get; init; }

    public int OpenTickets { get; init; }

    public IReadOnlyList<ReportsRepairStatusCountViewModel> StatusCounts { get; init; } =
        Array.Empty<ReportsRepairStatusCountViewModel>();

    public IReadOnlyList<ReportsRepairRecentRowViewModel> RecentTickets { get; init; } =
        Array.Empty<ReportsRepairRecentRowViewModel>();
}

public sealed class ReportsGlAccountTypeCountViewModel
{
    public GlAccountType Type { get; init; }

    public int Count { get; init; }
}

public sealed class ReportsTrialBalanceRowViewModel
{
    public string AccountCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public GlAccountType Type { get; init; }

    public decimal Balance { get; init; }
}

public sealed class ReportsFinancialPageViewModel
{
    public int ActiveGlAccountCount { get; init; }

    public int JournalEntryCount { get; init; }

    public int JournalEntriesThisMonth { get; init; }

    public decimal ExpensesThisMonth { get; init; }

    public IReadOnlyList<ReportsGlAccountTypeCountViewModel> AccountsByType { get; init; } =
        Array.Empty<ReportsGlAccountTypeCountViewModel>();

    public IReadOnlyList<ReportsTrialBalanceRowViewModel> TrialBalancePreview { get; init; } =
        Array.Empty<ReportsTrialBalanceRowViewModel>();
}
