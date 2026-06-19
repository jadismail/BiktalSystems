namespace Biktal.WebMVC.Models;

public sealed class DashboardKpiViewModel
{
    /// <summary>Net movement (credits − debits) on revenue-type GL lines for journal entries dated today (UTC).</summary>
    public decimal TodayRevenueFromJournals { get; init; }

    public int RepairsInProgress { get; init; }

    public int LowStockSkus { get; init; }

    public const int LowStockThreshold = 5;
}
