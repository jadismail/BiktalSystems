using Biktal.Domain.Repairs;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class InsightsController : Controller
{
    private readonly ApplicationDbContext _db;

    public InsightsController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static (DateTimeOffset MonthStart, DateOnly Today, string PeriodLabel) CurrentMonthPeriod()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (monthStart, today, $"{today:MMMM yyyy} (MTD)");
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Business insights";
        ViewData["Module"] = "Insights";
        ViewData["ModuleSubtitle"] = "How the business is performing — revenue, margin, expenses, and cash.";

        var (monthStart, today, periodLabel) = CurrentMonthPeriod();
        var monthEnd = today;

        var productRevenue = await (
            from line in _db.PosSaleLines.AsNoTracking()
            join sale in _db.PosSales.AsNoTracking() on line.PosSaleId equals sale.Id
            where sale.CompletedAtUtc >= monthStart
            select (decimal?)line.LineTotal).SumAsync(cancellationToken) ?? 0m;

        var productCost = await (
            from line in _db.PosSaleLines.AsNoTracking()
            join sale in _db.PosSales.AsNoTracking() on line.PosSaleId equals sale.Id
            where sale.CompletedAtUtc >= monthStart
            select (decimal?)line.LineCostTotal).SumAsync(cancellationToken) ?? 0m;

        var posRevenue = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= monthStart)
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        var posCount = await _db.PosSales.AsNoTracking()
            .CountAsync(s => s.CompletedAtUtc >= monthStart, cancellationToken);

        var deliveredRepairsQuery = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered && t.ModifiedAtUtc >= monthStart);

        var serviceRevenue = await deliveredRepairsQuery.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var serviceCost = await deliveredRepairsQuery.SumAsync(t => (decimal?)t.PartsCost, cancellationToken) ?? 0m;
        var repairCount = await deliveredRepairsQuery.CountAsync(cancellationToken);

        var totalRevenue = productRevenue + serviceRevenue;
        var grossProfit = totalRevenue - productCost - serviceCost;

        var expensesMtd = await _db.FinanceExpenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= DateOnly.FromDateTime(monthStart.UtcDateTime) && e.ExpenseDate <= monthEnd)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var cashCollected = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= monthStart && s.PaymentMethod.Contains("Cash"))
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;

        var expenseBreakdown = await _db.FinanceExpenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= DateOnly.FromDateTime(monthStart.UtcDateTime) && e.ExpenseDate <= monthEnd)
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Uncategorized" : e.Category)
            .Select(g => new InsightsExpenseCategoryRowViewModel
            {
                Category = g.Key,
                Amount = g.Sum(e => e.Amount),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        var revenueOverview = new List<InsightsChannelRowViewModel>
        {
            new()
            {
                Channel = "Products (POS)",
                Revenue = productRevenue,
                Cost = productCost,
                TransactionCount = posCount,
                IsLive = true
            },
            new()
            {
                Channel = "Services (repairs)",
                Revenue = serviceRevenue,
                Cost = serviceCost,
                TransactionCount = repairCount,
                IsLive = true
            },
            new() { Channel = "Online store", Revenue = 0m, TransactionCount = 0, IsLive = false },
            new() { Channel = "Other channels", Revenue = 0m, TransactionCount = 0, IsLive = false }
        };

        return View(new BusinessInsightsHubViewModel
        {
            PeriodLabel = periodLabel,
            TotalRevenue = totalRevenue,
            GrossProfit = grossProfit,
            ProductRevenue = productRevenue,
            ProductCost = productCost,
            ServiceRevenue = serviceRevenue,
            ServiceCost = serviceCost,
            ExpensesMtd = expensesMtd,
            CashCollectedMtd = cashCollected,
            CashPosition = cashCollected - expensesMtd,
            RevenueOverview = revenueOverview,
            ExpenseBreakdown = expenseBreakdown
        });
    }

    [HttpGet]
    public IActionResult RevenueMargin() => RedirectToActionPermanent("Sales", "Operations");

    [HttpGet]
    public IActionResult Inventory() => RedirectToActionPermanent("Inventory", "Operations");

    [HttpGet]
    public IActionResult Repairs() => RedirectToActionPermanent("Repairs", "Operations");

    [HttpGet]
    public IActionResult Customers() => RedirectToActionPermanent("Index", "Crm");

    [HttpGet]
    public IActionResult Staff() => RedirectToActionPermanent("Index", "Staff");
}
