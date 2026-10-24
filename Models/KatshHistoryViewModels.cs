namespace Biktal.WebMVC.Models;

public sealed class KatshHistoryPageViewModel
{
    public string Mode { get; init; } = "daily";
    public DateOnly SelectedDate { get; init; }
    public int SelectedYear { get; init; }
    public int SelectedMonth { get; init; }
    public string DateInputValue { get; init; } = "";
    public string MonthInputValue { get; init; } = "";
    public string PeriodLabel { get; init; } = "";
    public int SaleCount { get; init; }
    public decimal TotalUsd { get; init; }
    public decimal TotalLbp { get; init; }
    public IReadOnlyList<KatshHistorySaleRowViewModel> Sales { get; init; } = [];
}

public sealed class KatshHistorySaleRowViewModel
{
    public DateTime SoldAtLocal { get; init; }
    public DateOnly SaleDate { get; init; }
    public string Provider { get; init; } = "";
    public string Label { get; init; } = "";
    public string Currency { get; init; } = "";
    public decimal Amount { get; init; }
    public string? PaymentMethod { get; init; }
    public string? SoldBy { get; init; }
    public bool IsBalanceEntry { get; init; }
}
