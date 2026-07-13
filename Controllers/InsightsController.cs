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

    private static DateTimeOffset DayStartUtc(DateOnly day) =>
        new(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset DayEndUtc(DateOnly day) => DayStartUtc(day.AddDays(1));

    private sealed record PosSaleRow(DateTimeOffset CompletedAtUtc, decimal TotalAmount);

    private sealed record PosLineRow(
        DateTimeOffset CompletedAtUtc,
        decimal LineTotal,
        decimal LineCostTotal,
        int Quantity,
        string Category,
        string BrandName);

    private sealed record RepairRow(DateTimeOffset ModifiedAtUtc, decimal Revenue, decimal PartsCost);

    private sealed record ExpenseRow(DateOnly ExpenseDate, decimal Amount);

    private sealed record PeriodData(
        List<PosSaleRow> PosSales,
        List<PosLineRow> PosLines,
        List<RepairRow> Repairs,
        List<ExpenseRow> Expenses);

    private async Task<PeriodData> LoadPeriodAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateOnly startDate,
        DateOnly endDateInclusive,
        CancellationToken cancellationToken)
    {
        var posSales = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= startUtc && s.CompletedAtUtc < endUtc)
            .Select(s => new PosSaleRow(s.CompletedAtUtc, s.TotalAmount))
            .ToListAsync(cancellationToken);

        var posLines = await (
            from line in _db.PosSaleLines.AsNoTracking()
            join sale in _db.PosSales.AsNoTracking() on line.PosSaleId equals sale.Id
            join product in _db.CatalogProducts.AsNoTracking() on line.CatalogProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join brand in _db.CatalogBrands.AsNoTracking() on product!.BrandId equals brand.Id into brands
            from brand in brands.DefaultIfEmpty()
            where sale.CompletedAtUtc >= startUtc && sale.CompletedAtUtc < endUtc
            select new PosLineRow(
                sale.CompletedAtUtc,
                line.LineTotal,
                line.LineCostTotal,
                line.Quantity,
                product != null ? product.Category : "Uncategorized",
                brand != null ? brand.Name : "No brand"))
            .ToListAsync(cancellationToken);

        var repairs = await _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered
                        && t.ModifiedAtUtc >= startUtc
                        && t.ModifiedAtUtc < endUtc)
            .Select(t => new RepairRow(t.ModifiedAtUtc!.Value, t.EstimatedPrice ?? 0m, t.PartsCost ?? 0m))
            .ToListAsync(cancellationToken);

        var expenses = await _db.FinanceExpenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= startDate && e.ExpenseDate <= endDateInclusive)
            .Select(e => new ExpenseRow(e.ExpenseDate, e.Amount))
            .ToListAsync(cancellationToken);

        return new PeriodData(posSales, posLines, repairs, expenses);
    }

    private static List<InsightsBreakdownRowViewModel> BuildBreakdown(
        IEnumerable<PosLineRow> lines,
        Func<PosLineRow, string> keySelector) =>
        lines
            .GroupBy(x =>
            {
                var key = keySelector(x);
                return string.IsNullOrWhiteSpace(key) ? "Uncategorized" : key;
            })
            .Select(g => new InsightsBreakdownRowViewModel
            {
                Label = g.Key,
                Revenue = g.Sum(x => x.LineTotal),
                Cost = g.Sum(x => x.LineCostTotal),
                UnitsSold = g.Sum(x => x.Quantity)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Daily));

    [HttpGet]
    public async Task<IActionResult> Daily(DateOnly? date, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Daily insights";
        ViewData["Module"] = "Insights";
        ViewData["ModuleSubtitle"] = "Pick a day to see revenue, profit, the busiest hours, and every transaction.";

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var dayStart = DayStartUtc(day);
        var dayEnd = DayEndUtc(day);

        var data = await LoadPeriodAsync(dayStart, dayEnd, day, day, cancellationToken);

        var posSalesTotal = data.PosSales.Sum(x => x.TotalAmount);
        var productCost = data.PosLines.Sum(x => x.LineCostTotal);
        var repairRevenue = data.Repairs.Sum(x => x.Revenue);
        var repairCost = data.Repairs.Sum(x => x.PartsCost);
        var totalRevenue = posSalesTotal + repairRevenue;
        var grossProfit = posSalesTotal - productCost + (repairRevenue - repairCost);

        var hourly = new List<InsightsBarBucketViewModel>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            var posInHour = data.PosSales.Where(s => s.CompletedAtUtc.UtcDateTime.Hour == hour).ToList();
            var repairsInHour = data.Repairs.Where(r => r.ModifiedAtUtc.UtcDateTime.Hour == hour).ToList();
            var revenue = posInHour.Sum(s => s.TotalAmount) + repairsInHour.Sum(r => r.Revenue);
            hourly.Add(new InsightsBarBucketViewModel
            {
                Label = new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt"),
                Revenue = revenue,
                Count = posInHour.Count + repairsInHour.Count
            });
        }

        var posRecords = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= dayStart && s.CompletedAtUtc < dayEnd)
            .Select(s => new InsightsRecordRowViewModel
            {
                TimeUtc = s.CompletedAtUtc,
                Type = "POS sale",
                Reference = s.SaleNumber,
                Description = string.IsNullOrWhiteSpace(s.CustomerName) ? "Walk-in customer" : s.CustomerName,
                PaymentMethod = s.PaymentMethod,
                Amount = s.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var repairRecords = await _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered
                        && t.ModifiedAtUtc >= dayStart
                        && t.ModifiedAtUtc < dayEnd)
            .Select(t => new InsightsRecordRowViewModel
            {
                TimeUtc = t.ModifiedAtUtc!.Value,
                Type = "Repair delivered",
                Reference = t.TicketNumber,
                Description = string.IsNullOrWhiteSpace(t.DeviceSummary) ? t.CustomerName : t.DeviceSummary,
                PaymentMethod = string.Empty,
                Amount = t.EstimatedPrice ?? 0m
            })
            .ToListAsync(cancellationToken);

        var records = posRecords.Concat(repairRecords)
            .OrderBy(r => r.TimeUtc)
            .ToList();

        return View(new InsightsDailyViewModel
        {
            Date = day,
            TotalRevenue = totalRevenue,
            PosSales = posSalesTotal,
            RepairDelivered = repairRevenue,
            GrossProfit = grossProfit,
            PosSaleCount = data.PosSales.Count,
            RepairCount = data.Repairs.Count,
            Hourly = hourly,
            Records = records
        });
    }

    [HttpGet]
    public async Task<IActionResult> Monthly(string? period, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Monthly insights";
        ViewData["Module"] = "Insights";
        ViewData["ModuleSubtitle"] = "Pick a month for revenue, profit, expenses, weekly trend, and top categories & brands.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = today.Year;
        var month = today.Month;
        if (!string.IsNullOrWhiteSpace(period)
            && DateTime.TryParse($"{period}-01", out var parsed))
        {
            year = parsed.Year;
            month = parsed.Month;
        }

        var monthStart = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);
        var startDate = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var endDateInclusive = new DateOnly(year, month, daysInMonth);

        var data = await LoadPeriodAsync(monthStart, monthEnd, startDate, endDateInclusive, cancellationToken);

        var posSalesTotal = data.PosSales.Sum(x => x.TotalAmount);
        var productCost = data.PosLines.Sum(x => x.LineCostTotal);
        var repairRevenue = data.Repairs.Sum(x => x.Revenue);
        var repairCost = data.Repairs.Sum(x => x.PartsCost);
        var totalRevenue = posSalesTotal + repairRevenue;
        var grossProfit = posSalesTotal - productCost + (repairRevenue - repairCost);
        var expenses = data.Expenses.Sum(x => x.Amount);

        var weekCount = (int)Math.Ceiling(daysInMonth / 7.0);
        var weekly = new List<InsightsBarBucketViewModel>(weekCount);
        for (var week = 0; week < weekCount; week++)
        {
            var posInWeek = data.PosSales.Where(s => WeekIndex(s.CompletedAtUtc) == week).ToList();
            var costInWeek = data.PosLines.Where(l => WeekIndex(l.CompletedAtUtc) == week).Sum(l => l.LineCostTotal);
            var repairsInWeek = data.Repairs.Where(r => WeekIndex(r.ModifiedAtUtc) == week).ToList();
            var weekPos = posInWeek.Sum(s => s.TotalAmount);
            var weekRepair = repairsInWeek.Sum(r => r.Revenue);
            var weekRepairCost = repairsInWeek.Sum(r => r.PartsCost);
            var weekExpenses = data.Expenses.Where(e => (e.ExpenseDate.Day - 1) / 7 == week).Sum(e => e.Amount);
            weekly.Add(new InsightsBarBucketViewModel
            {
                Label = $"Week {week + 1}",
                Revenue = weekPos + weekRepair,
                GrossProfit = weekPos - costInWeek + (weekRepair - weekRepairCost),
                Expenses = weekExpenses,
                Count = posInWeek.Count + repairsInWeek.Count
            });
        }

        return View(new InsightsMonthlyViewModel
        {
            Year = year,
            Month = month,
            PeriodLabel = monthStart.ToString("MMMM yyyy"),
            TotalRevenue = totalRevenue,
            PosSales = posSalesTotal,
            RepairDelivered = repairRevenue,
            GrossProfit = grossProfit,
            Expenses = expenses,
            PosSaleCount = data.PosSales.Count,
            RepairCount = data.Repairs.Count,
            Weekly = weekly,
            Categories = BuildBreakdown(data.PosLines, x => x.Category),
            Brands = BuildBreakdown(data.PosLines, x => x.BrandName)
        });

        static int WeekIndex(DateTimeOffset value) => (value.UtcDateTime.Day - 1) / 7;
    }

    [HttpGet]
    public async Task<IActionResult> Yearly(int? year, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Yearly insights";
        ViewData["Module"] = "Insights";
        ViewData["ModuleSubtitle"] = "Pick a year for revenue, profit, expenses, monthly trend, and top categories & brands.";

        var currentYear = DateTime.UtcNow.Year;
        var selectedYear = year ?? currentYear;

        var firstSaleUtc = await _db.PosSales.AsNoTracking()
            .OrderBy(s => s.CompletedAtUtc)
            .Select(s => (DateTimeOffset?)s.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var earliestYear = firstSaleUtc?.UtcDateTime.Year ?? currentYear;
        if (earliestYear > currentYear) earliestYear = currentYear;
        var availableYears = Enumerable.Range(earliestYear, currentYear - earliestYear + 1)
            .Reverse()
            .ToList();
        if (!availableYears.Contains(selectedYear))
        {
            availableYears.Add(selectedYear);
            availableYears = availableYears.OrderByDescending(y => y).ToList();
        }

        var yearStart = new DateTimeOffset(selectedYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = yearStart.AddYears(1);
        var startDate = new DateOnly(selectedYear, 1, 1);
        var endDateInclusive = new DateOnly(selectedYear, 12, 31);

        var data = await LoadPeriodAsync(yearStart, yearEnd, startDate, endDateInclusive, cancellationToken);

        var posSalesTotal = data.PosSales.Sum(x => x.TotalAmount);
        var productCost = data.PosLines.Sum(x => x.LineCostTotal);
        var repairRevenue = data.Repairs.Sum(x => x.Revenue);
        var repairCost = data.Repairs.Sum(x => x.PartsCost);
        var totalRevenue = posSalesTotal + repairRevenue;
        var grossProfit = posSalesTotal - productCost + (repairRevenue - repairCost);
        var expenses = data.Expenses.Sum(x => x.Amount);

        var monthly = new List<InsightsBarBucketViewModel>(12);
        for (var m = 1; m <= 12; m++)
        {
            var posInMonth = data.PosSales.Where(s => s.CompletedAtUtc.UtcDateTime.Month == m).ToList();
            var costInMonth = data.PosLines.Where(l => l.CompletedAtUtc.UtcDateTime.Month == m).Sum(l => l.LineCostTotal);
            var repairsInMonth = data.Repairs.Where(r => r.ModifiedAtUtc.UtcDateTime.Month == m).ToList();
            var monthPos = posInMonth.Sum(s => s.TotalAmount);
            var monthRepair = repairsInMonth.Sum(r => r.Revenue);
            var monthRepairCost = repairsInMonth.Sum(r => r.PartsCost);
            var monthExpenses = data.Expenses.Where(e => e.ExpenseDate.Month == m).Sum(e => e.Amount);
            monthly.Add(new InsightsBarBucketViewModel
            {
                Label = new DateTime(2000, m, 1).ToString("MMM"),
                Revenue = monthPos + monthRepair,
                GrossProfit = monthPos - costInMonth + (monthRepair - monthRepairCost),
                Expenses = monthExpenses,
                Count = posInMonth.Count + repairsInMonth.Count
            });
        }

        return View(new InsightsYearlyViewModel
        {
            Year = selectedYear,
            PeriodLabel = selectedYear.ToString(),
            AvailableYears = availableYears,
            TotalRevenue = totalRevenue,
            PosSales = posSalesTotal,
            RepairDelivered = repairRevenue,
            GrossProfit = grossProfit,
            Expenses = expenses,
            PosSaleCount = data.PosSales.Count,
            RepairCount = data.Repairs.Count,
            Monthly = monthly,
            Categories = BuildBreakdown(data.PosLines, x => x.Category),
            Brands = BuildBreakdown(data.PosLines, x => x.BrandName)
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
