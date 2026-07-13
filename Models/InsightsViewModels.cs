using Biktal.Domain.Inventory;
using Biktal.Domain.Repairs;

namespace Biktal.WebMVC.Models;

public sealed class BusinessInsightsHubViewModel
{
    public string PeriodLabel { get; init; } = "Month to date";

    public decimal TotalRevenue { get; init; }

    public decimal GrossProfit { get; init; }

    public decimal GrossProfitPercent => TotalRevenue > 0m ? GrossProfit / TotalRevenue * 100m : 0m;

    public decimal ProductRevenue { get; init; }

    public decimal ProductCost { get; init; }

    public decimal ProductMargin => ProductRevenue - ProductCost;

    public decimal ProductMarginPercent => ProductRevenue > 0m ? ProductMargin / ProductRevenue * 100m : 0m;

    public decimal ServiceRevenue { get; init; }

    public decimal ServiceCost { get; init; }

    public decimal ServiceMargin => ServiceRevenue - ServiceCost;

    public decimal ServiceMarginPercent => ServiceRevenue > 0m ? ServiceMargin / ServiceRevenue * 100m : 0m;

    public decimal ExpensesMtd { get; init; }

    public decimal NetAfterExpenses => GrossProfit - ExpensesMtd;

    /// <summary>Estimated cash in (POS cash payments) minus logged expenses MTD.</summary>
    public decimal CashPosition { get; init; }

    public decimal CashCollectedMtd { get; init; }

    public IReadOnlyList<InsightsChannelRowViewModel> RevenueOverview { get; init; } =
        Array.Empty<InsightsChannelRowViewModel>();

    public IReadOnlyList<InsightsExpenseCategoryRowViewModel> ExpenseBreakdown { get; init; } =
        Array.Empty<InsightsExpenseCategoryRowViewModel>();
}

public sealed class InsightsExpenseCategoryRowViewModel
{
    public string Category { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public int Count { get; init; }
}

/// <summary>A single revenue/profit slice for a category or brand drop-down.</summary>
public sealed class InsightsBreakdownRowViewModel
{
    public string Label { get; init; } = string.Empty;

    public decimal Revenue { get; init; }

    public decimal Cost { get; init; }

    public decimal Profit => Revenue - Cost;

    public decimal MarginPercent => Revenue > 0m ? Profit / Revenue * 100m : 0m;

    public int UnitsSold { get; init; }
}

/// <summary>Drives one selectable "top category / top brand" drop-down panel.</summary>
public sealed class InsightsBreakdownPanelViewModel
{
    public string Title { get; init; } = string.Empty;

    public string SelectLabel { get; init; } = string.Empty;

    /// <summary>When true the panel is sorted by (and highlights) profit; otherwise revenue.</summary>
    public bool ByProfit { get; init; }

    public IReadOnlyList<InsightsBreakdownRowViewModel> Rows { get; init; } =
        Array.Empty<InsightsBreakdownRowViewModel>();
}

/// <summary>A bar in a time-bucketed chart (hour, week, or month).</summary>
public sealed class InsightsBarBucketViewModel
{
    public string Label { get; init; } = string.Empty;

    public decimal Revenue { get; init; }

    public decimal GrossProfit { get; init; }

    public decimal Expenses { get; init; }

    public int Count { get; init; }
}

/// <summary>A single transaction shown in the daily activity log.</summary>
public sealed class InsightsRecordRowViewModel
{
    public DateTimeOffset TimeUtc { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string PaymentMethod { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

public sealed class InsightsDailyViewModel
{
    public DateOnly Date { get; init; }

    public string DateInputValue => Date.ToString("yyyy-MM-dd");

    public string PeriodLabel => Date.ToString("dddd, dd MMMM yyyy");

    public decimal TotalRevenue { get; init; }

    public decimal PosSales { get; init; }

    public decimal RepairDelivered { get; init; }

    public decimal GrossProfit { get; init; }

    public decimal GrossProfitPercent => TotalRevenue > 0m ? GrossProfit / TotalRevenue * 100m : 0m;

    public int PosSaleCount { get; init; }

    public int RepairCount { get; init; }

    public IReadOnlyList<InsightsBarBucketViewModel> Hourly { get; init; } =
        Array.Empty<InsightsBarBucketViewModel>();

    public IReadOnlyList<InsightsRecordRowViewModel> Records { get; init; } =
        Array.Empty<InsightsRecordRowViewModel>();
}

public sealed class InsightsMonthlyViewModel
{
    public int Year { get; init; }

    public int Month { get; init; }

    public string MonthInputValue => $"{Year:D4}-{Month:D2}";

    public string PeriodLabel { get; init; } = string.Empty;

    public decimal TotalRevenue { get; init; }

    public decimal PosSales { get; init; }

    public decimal RepairDelivered { get; init; }

    public decimal GrossProfit { get; init; }

    public decimal GrossProfitPercent => TotalRevenue > 0m ? GrossProfit / TotalRevenue * 100m : 0m;

    public decimal Expenses { get; init; }

    public decimal NetProfit => GrossProfit - Expenses;

    public int PosSaleCount { get; init; }

    public int RepairCount { get; init; }

    public IReadOnlyList<InsightsBarBucketViewModel> Weekly { get; init; } =
        Array.Empty<InsightsBarBucketViewModel>();

    public IReadOnlyList<InsightsBreakdownRowViewModel> Categories { get; init; } =
        Array.Empty<InsightsBreakdownRowViewModel>();

    public IReadOnlyList<InsightsBreakdownRowViewModel> Brands { get; init; } =
        Array.Empty<InsightsBreakdownRowViewModel>();
}

public sealed class InsightsYearlyViewModel
{
    public int Year { get; init; }

    public string PeriodLabel { get; init; } = string.Empty;

    public IReadOnlyList<int> AvailableYears { get; init; } = Array.Empty<int>();

    public decimal TotalRevenue { get; init; }

    public decimal PosSales { get; init; }

    public decimal RepairDelivered { get; init; }

    public decimal GrossProfit { get; init; }

    public decimal GrossProfitPercent => TotalRevenue > 0m ? GrossProfit / TotalRevenue * 100m : 0m;

    public decimal Expenses { get; init; }

    public decimal NetProfit => GrossProfit - Expenses;

    public int PosSaleCount { get; init; }

    public int RepairCount { get; init; }

    public IReadOnlyList<InsightsBarBucketViewModel> Monthly { get; init; } =
        Array.Empty<InsightsBarBucketViewModel>();

    public IReadOnlyList<InsightsBreakdownRowViewModel> Categories { get; init; } =
        Array.Empty<InsightsBreakdownRowViewModel>();

    public IReadOnlyList<InsightsBreakdownRowViewModel> Brands { get; init; } =
        Array.Empty<InsightsBreakdownRowViewModel>();
}

public sealed class InsightsMarginRowViewModel
{
    public string Label { get; init; } = string.Empty;

    public decimal Revenue { get; init; }

    public decimal Cost { get; init; }

    public decimal MarginAmount => Revenue - Cost;

    public decimal MarginPercent => Revenue > 0m ? (Revenue - Cost) / Revenue * 100m : 0m;
}

public sealed class InsightsChannelRowViewModel
{
    public string Channel { get; init; } = string.Empty;

    public decimal Revenue { get; init; }

    public decimal Cost { get; init; }

    public decimal MarginAmount => Revenue - Cost;

    public decimal MarginPercent => Revenue > 0m ? MarginAmount / Revenue * 100m : 0m;

    public int TransactionCount { get; init; }

    public bool IsLive { get; init; }
}

public sealed class InsightsTopProductRowViewModel
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public int QuantitySold { get; init; }

    public decimal Revenue { get; init; }

    public decimal Margin { get; init; }
}

public sealed class InsightsRevenueMarginViewModel
{
    public string PeriodLabel { get; init; } = string.Empty;

    public decimal TotalRevenue { get; init; }

    public decimal TotalCost { get; init; }

    public decimal TotalMargin => TotalRevenue - TotalCost;

    public decimal MarginPercent => TotalRevenue > 0m ? TotalMargin / TotalRevenue * 100m : 0m;

    public IReadOnlyList<InsightsMarginRowViewModel> RevenueByCategory { get; init; } =
        Array.Empty<InsightsMarginRowViewModel>();

    public IReadOnlyList<InsightsMarginRowViewModel> MarginByCategory { get; init; } =
        Array.Empty<InsightsMarginRowViewModel>();

    public IReadOnlyList<InsightsMarginRowViewModel> RevenueByBrand { get; init; } =
        Array.Empty<InsightsMarginRowViewModel>();

    public IReadOnlyList<InsightsMarginRowViewModel> MarginByBrand { get; init; } =
        Array.Empty<InsightsMarginRowViewModel>();

    public IReadOnlyList<InsightsChannelRowViewModel> RevenueByChannel { get; init; } =
        Array.Empty<InsightsChannelRowViewModel>();

    public IReadOnlyList<InsightsTopProductRowViewModel> TopProducts { get; init; } =
        Array.Empty<InsightsTopProductRowViewModel>();

    public bool HasPosSales { get; init; }
}

public sealed class InsightsInventoryLowStockRowViewModel
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }
}

public sealed class InsightsInventoryCategoryRowViewModel
{
    public string Category { get; init; } = string.Empty;

    public int SkuCount { get; init; }

    public int TotalUnits { get; init; }

    public decimal RetailValue { get; init; }

    public decimal CostValue { get; init; }
}

public sealed class InsightsMovementSummaryRowViewModel
{
    public string TypeLabel { get; init; } = string.Empty;

    public int Count { get; init; }

    public int NetUnits { get; init; }

    public decimal TotalCost { get; init; }
}

public sealed class InsightsInventoryViewModel
{
    public int CatalogSkuCount { get; init; }

    public int TotalStockUnits { get; init; }

    public decimal InventoryRetailValue { get; init; }

    public decimal InventoryCostValue { get; init; }

    public int LowStockSkuCount { get; init; }

    public int LowStockThreshold { get; init; } = 5;

    public IReadOnlyList<InsightsInventoryLowStockRowViewModel> LowStockRows { get; init; } =
        Array.Empty<InsightsInventoryLowStockRowViewModel>();

    public IReadOnlyList<InsightsInventoryCategoryRowViewModel> CategoryValuation { get; init; } =
        Array.Empty<InsightsInventoryCategoryRowViewModel>();

    public IReadOnlyList<InsightsMovementSummaryRowViewModel> MovementSummaryMtd { get; init; } =
        Array.Empty<InsightsMovementSummaryRowViewModel>();
}

public sealed class InsightsRepairStatusCountViewModel
{
    public RepairTicketStatus Status { get; init; }

    public int Count { get; init; }
}

public sealed class InsightsRepairRecentRowViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string DeviceSummary { get; init; } = string.Empty;

    public RepairTicketStatus Status { get; init; }

    public decimal? EstimatedPrice { get; init; }

    public decimal? PartsCost { get; init; }

    public decimal? MarginAmount =>
        EstimatedPrice is { } rev && PartsCost is { } cost ? rev - cost : null;

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class InsightsRepairsViewModel
{
    public string PeriodLabel { get; init; } = string.Empty;

    public int TotalTickets { get; init; }

    public int OpenTickets { get; init; }

    public decimal EstimatedOpenRevenue { get; init; }

    public decimal EstimatedOpenPartsCost { get; init; }

    public decimal EstimatedOpenMargin => EstimatedOpenRevenue - EstimatedOpenPartsCost;

    public decimal DeliveredRevenueMtd { get; init; }

    public decimal DeliveredPartsCostMtd { get; init; }

    public decimal DeliveredMarginMtd => DeliveredRevenueMtd - DeliveredPartsCostMtd;

    public decimal DeliveredMarginPercent =>
        DeliveredRevenueMtd > 0m ? DeliveredMarginMtd / DeliveredRevenueMtd * 100m : 0m;

    public int DeliveredCountMtd { get; init; }

    public int TicketsWithMarginData { get; init; }

    public IReadOnlyList<InsightsRepairStatusCountViewModel> StatusCounts { get; init; } =
        Array.Empty<InsightsRepairStatusCountViewModel>();

    public IReadOnlyList<InsightsRepairRecentRowViewModel> RecentTickets { get; init; } =
        Array.Empty<InsightsRepairRecentRowViewModel>();
}

public sealed class InsightsCustomerSegmentRowViewModel
{
    public string Segment { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class InsightsCustomersViewModel
{
    public int ActiveCustomers { get; init; }

    public int NewCustomersMtd { get; init; }

    public int OpenSupportTickets { get; init; }

    public int ActiveCampaigns { get; init; }

    public long TotalLoyaltyPoints { get; init; }

    public IReadOnlyList<InsightsCustomerSegmentRowViewModel> TicketsByStatus { get; init; } =
        Array.Empty<InsightsCustomerSegmentRowViewModel>();
}

public sealed class InsightsStaffRowViewModel
{
    public string DisplayName { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int AttendanceDaysMtd { get; init; }

    public decimal HoursMtd { get; init; }
}

public sealed class InsightsStaffViewModel
{
    public int ActiveStaff { get; init; }

    public int OnLeaveStaff { get; init; }

    public decimal PayrollDraftTotal { get; init; }

    public int AttendanceEntriesMtd { get; init; }

    public IReadOnlyList<InsightsStaffRowViewModel> Team { get; init; } = Array.Empty<InsightsStaffRowViewModel>();
}
