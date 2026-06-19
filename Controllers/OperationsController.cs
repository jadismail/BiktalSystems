using Biktal.Domain.Inventory;
using Biktal.Domain.Repairs;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class OperationsController : Controller
{
    private const int LowStockThreshold = 5;

    private readonly ApplicationDbContext _db;

    public OperationsController(ApplicationDbContext db)
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

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Operations";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Daily store activity — sales, inventory, repairs, and supply chain.";

        var (monthStart, today, periodLabel) = CurrentMonthPeriod();
        var dayStart = DayStartUtc(today);
        var dayEnd = DayEndUtc(today);

        var dailyPos = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= dayStart && s.CompletedAtUtc < dayEnd)
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(s => s.TotalAmount), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);

        var monthlyPos = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= monthStart)
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(s => s.TotalAmount), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);

        var lowStock = await _db.CatalogProducts.AsNoTracking()
            .CountAsync(p => p.StockQuantity <= LowStockThreshold, cancellationToken);

        var openPo = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(po => po.Status != PurchaseOrderStatus.Received && po.Status != PurchaseOrderStatus.Cancelled,
                cancellationToken);

        var movementsMtd = await _db.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.CreatedAtUtc >= monthStart, cancellationToken);

        return View(new OperationsHubViewModel
        {
            PeriodLabel = periodLabel,
            DailySalesToday = dailyPos?.Revenue ?? 0m,
            DailySaleCountToday = dailyPos?.Count ?? 0,
            MonthlySalesMtd = monthlyPos?.Revenue ?? 0m,
            MonthlySaleCountMtd = monthlyPos?.Count ?? 0,
            LowStockSkuCount = lowStock,
            OpenPurchaseOrderCount = openPo,
            InventoryMovementCountMtd = movementsMtd
        });
    }

    [HttpGet]
    public async Task<IActionResult> DailySales(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Daily sales";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Today's completed POS sales and delivered repairs.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayStart = DayStartUtc(today);
        var dayEnd = DayEndUtc(today);

        var posRevenue = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= dayStart && s.CompletedAtUtc < dayEnd)
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        var posCount = await _db.PosSales.AsNoTracking()
            .CountAsync(s => s.CompletedAtUtc >= dayStart && s.CompletedAtUtc < dayEnd, cancellationToken);

        var deliveredRepairs = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered
                        && t.ModifiedAtUtc >= dayStart
                        && t.ModifiedAtUtc < dayEnd);

        var repairRevenue = await deliveredRepairs.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var repairCount = await deliveredRepairs.CountAsync(cancellationToken);

        return View(new OperationsDailySalesViewModel
        {
            Date = today,
            PosRevenue = posRevenue,
            PosSaleCount = posCount,
            RepairRevenue = repairRevenue,
            RepairCount = repairCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> MonthlySales(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Monthly sales";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Month-to-date POS and repair revenue.";

        var (monthStart, _, periodLabel) = CurrentMonthPeriod();

        var posRevenue = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= monthStart)
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        var posCount = await _db.PosSales.AsNoTracking()
            .CountAsync(s => s.CompletedAtUtc >= monthStart, cancellationToken);

        var deliveredRepairs = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered && t.ModifiedAtUtc >= monthStart);

        var repairRevenue = await deliveredRepairs.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var repairCount = await deliveredRepairs.CountAsync(cancellationToken);

        return View(new OperationsMonthlySalesViewModel
        {
            PeriodLabel = periodLabel,
            PosRevenue = posRevenue,
            PosSaleCount = posCount,
            RepairRevenue = repairRevenue,
            RepairCount = repairCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> Sales(CancellationToken cancellationToken) =>
        View(await BuildSalesPageAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> ProductProfitability(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Product profitability";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Margin by category, brand, and top SKUs.";
        return View(await BuildSalesPageAsync(cancellationToken));
    }

    [HttpGet]
    public IActionResult SalesTrends() => ComingSoon(
        "Sales trends",
        "Daily and weekly revenue charts across POS and repair channels.");

    [HttpGet]
    public async Task<IActionResult> Inventory(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Real-time inventory";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Stock health, valuation, and movement activity.";

        var (monthStart, _, _) = CurrentMonthPeriod();

        var catalogSkuCount = await _db.CatalogProducts.AsNoTracking().CountAsync(cancellationToken);
        var totalStockUnits = await _db.CatalogProducts.AsNoTracking()
            .SumAsync(p => (int?)p.StockQuantity, cancellationToken) ?? 0;
        var retail = await _db.CatalogProducts.AsNoTracking()
            .SumAsync(p => (decimal?)(p.Price * p.StockQuantity), cancellationToken) ?? 0m;
        var cost = await _db.CatalogProducts.AsNoTracking()
            .SumAsync(p => (decimal?)(p.Cost * p.StockQuantity), cancellationToken) ?? 0m;

        var lowStock = await _db.CatalogProducts.AsNoTracking()
            .Where(p => p.StockQuantity <= LowStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Select(p => new InsightsInventoryLowStockRowViewModel
            {
                Sku = p.Sku,
                Name = p.Name,
                Category = p.Category,
                StockQuantity = p.StockQuantity,
                Price = p.Price
            })
            .ToListAsync(cancellationToken);

        var categoryValuation = await _db.CatalogProducts.AsNoTracking()
            .GroupBy(p => p.Category)
            .Select(g => new InsightsInventoryCategoryRowViewModel
            {
                Category = g.Key,
                SkuCount = g.Count(),
                TotalUnits = g.Sum(p => p.StockQuantity),
                RetailValue = g.Sum(p => p.Price * p.StockQuantity),
                CostValue = g.Sum(p => p.Cost * p.StockQuantity)
            })
            .OrderByDescending(x => x.RetailValue)
            .ToListAsync(cancellationToken);

        var movementSummary = (await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.CreatedAtUtc >= monthStart)
            .GroupBy(m => m.Type)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
                NetUnits = g.Sum(m => m.QuantityDelta),
                TotalCost = g.Sum(m => m.TotalCost)
            })
            .ToListAsync(cancellationToken))
            .Select(g => new InsightsMovementSummaryRowViewModel
            {
                TypeLabel = InventoryMovementLabels.For(g.Key),
                Count = g.Count,
                NetUnits = g.NetUnits,
                TotalCost = g.TotalCost
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        return View(new InsightsInventoryViewModel
        {
            CatalogSkuCount = catalogSkuCount,
            TotalStockUnits = totalStockUnits,
            InventoryRetailValue = retail,
            InventoryCostValue = cost,
            LowStockSkuCount = lowStock.Count,
            LowStockThreshold = LowStockThreshold,
            LowStockRows = lowStock,
            CategoryValuation = categoryValuation,
            MovementSummaryMtd = movementSummary
        });
    }

    [HttpGet]
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Low stock alerts";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = $"SKUs at or below {LowStockThreshold} units on hand.";

        var lowStock = await _db.CatalogProducts.AsNoTracking()
            .Where(p => p.StockQuantity <= LowStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .Select(p => new InsightsInventoryLowStockRowViewModel
            {
                Sku = p.Sku,
                Name = p.Name,
                Category = p.Category,
                StockQuantity = p.StockQuantity,
                Price = p.Price
            })
            .ToListAsync(cancellationToken);

        return View(lowStock);
    }

    [HttpGet]
    public IActionResult InventoryAging() => ComingSoon(
        "Inventory aging",
        "Days on shelf and slow-moving SKU reports from movement history.");

    [HttpGet]
    public async Task<IActionResult> Repairs(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Repair revenue";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Service desk funnel with parts cost and repair margin.";

        var (monthStart, _, periodLabel) = CurrentMonthPeriod();

        var total = await _db.RepairTickets.AsNoTracking().CountAsync(cancellationToken);
        var open = await _db.RepairTickets.AsNoTracking()
            .CountAsync(t => t.Status != RepairTicketStatus.Delivered && t.Status != RepairTicketStatus.Cancelled,
                cancellationToken);

        var openQuery = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status != RepairTicketStatus.Delivered && t.Status != RepairTicketStatus.Cancelled);

        var pipelineRevenue = await openQuery.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var pipelinePartsCost = await openQuery.SumAsync(t => (decimal?)t.PartsCost, cancellationToken) ?? 0m;

        var deliveredQuery = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered && t.ModifiedAtUtc >= monthStart);

        var deliveredRevenueMtd = await deliveredQuery.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var deliveredPartsCostMtd = await deliveredQuery.SumAsync(t => (decimal?)t.PartsCost, cancellationToken) ?? 0m;
        var deliveredCountMtd = await deliveredQuery.CountAsync(cancellationToken);

        var ticketsWithMarginData = await _db.RepairTickets.AsNoTracking()
            .CountAsync(t => t.EstimatedPrice != null && t.PartsCost != null, cancellationToken);

        var statusCounts = await _db.RepairTickets.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new InsightsRepairStatusCountViewModel { Status = g.Key, Count = g.Count() })
            .OrderBy(x => x.Status)
            .ToListAsync(cancellationToken);

        var recent = await _db.RepairTickets.AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(22)
            .Select(t => new InsightsRepairRecentRowViewModel
            {
                Id = t.Id,
                TicketNumber = t.TicketNumber,
                CustomerName = t.CustomerName,
                DeviceSummary = t.DeviceSummary,
                Status = t.Status,
                EstimatedPrice = t.EstimatedPrice,
                PartsCost = t.PartsCost,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return View(new InsightsRepairsViewModel
        {
            PeriodLabel = periodLabel,
            TotalTickets = total,
            OpenTickets = open,
            EstimatedOpenRevenue = pipelineRevenue,
            EstimatedOpenPartsCost = pipelinePartsCost,
            DeliveredRevenueMtd = deliveredRevenueMtd,
            DeliveredPartsCostMtd = deliveredPartsCostMtd,
            DeliveredCountMtd = deliveredCountMtd,
            TicketsWithMarginData = ticketsWithMarginData,
            StatusCounts = statusCounts,
            RecentTickets = recent
        });
    }

    [HttpGet]
    public IActionResult TechnicianPerformance() => ComingSoon(
        "Technician performance",
        "Tickets closed, turnaround time, and repair revenue by technician.");

    [HttpGet]
    public async Task<IActionResult> PurchaseOrders(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Purchase orders";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Open PO pipeline and recent supplier orders.";

        var (monthStart, _, _) = CurrentMonthPeriod();

        var openOrders = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.Status != PurchaseOrderStatus.Received && po.Status != PurchaseOrderStatus.Cancelled)
            .Include(po => po.Lines)
            .ToListAsync(cancellationToken);

        var draftCount = openOrders.Count(po => po.Status == PurchaseOrderStatus.Draft);
        var openValue = openOrders.Sum(po => po.Lines.Sum(l => l.UnitCost * l.QuantityOrdered));

        var receivedMtd = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(po => po.Status == PurchaseOrderStatus.Received && po.ModifiedAtUtc >= monthStart,
                cancellationToken);

        var recentRaw = await _db.PurchaseOrders.AsNoTracking()
            .Include(po => po.Lines)
            .OrderByDescending(po => po.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var recent = recentRaw.Select(po => new OperationsPurchaseOrderRowViewModel
        {
            Id = po.Id,
            OrderNumber = po.PoNumber,
            SupplierName = po.SupplierName,
            Status = PurchaseOrderStatusReportLabels.Title(po.Status),
            TotalAmount = po.Lines.Sum(l => l.UnitCost * l.QuantityOrdered),
            OrderDate = DateOnly.FromDateTime(po.CreatedAtUtc.UtcDateTime)
        }).ToList();

        return View(new OperationsPurchaseOrdersViewModel
        {
            OpenCount = openOrders.Count,
            DraftCount = draftCount,
            ReceivedMtdCount = receivedMtd,
            OpenOrderValue = openValue,
            RecentOrders = recent
        });
    }

    [HttpGet]
    public async Task<IActionResult> StockMovement(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Stock movement";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Inventory adjustments, receipts, and transfers MTD.";

        var (monthStart, _, periodLabel) = CurrentMonthPeriod();

        var movementSummary = (await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.CreatedAtUtc >= monthStart)
            .GroupBy(m => m.Type)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
                NetUnits = g.Sum(m => m.QuantityDelta),
                TotalCost = g.Sum(m => m.TotalCost)
            })
            .ToListAsync(cancellationToken))
            .Select(g => new InsightsMovementSummaryRowViewModel
            {
                TypeLabel = InventoryMovementLabels.For(g.Key),
                Count = g.Count,
                NetUnits = g.NetUnits,
                TotalCost = g.TotalCost
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var totalMovements = movementSummary.Sum(m => m.Count);

        ViewBag.PeriodLabel = periodLabel;
        ViewBag.TotalMovements = totalMovements;
        return View(movementSummary);
    }

    [HttpGet]
    public IActionResult ImeiTracking() => ComingSoon(
        "IMEI tracking",
        "Serial-level device registry linked to sales, repairs, and warranty.");

    [HttpGet]
    public IActionResult WarrantyTracking() => ComingSoon(
        "Warranty tracking",
        "Active warranties, expiry alerts, and claim history per device.");

    private IActionResult ComingSoon(string title, string description)
    {
        ViewData["Title"] = title;
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Planned for a future release.";
        return View("ComingSoon", new OperationsComingSoonViewModel
        {
            FeatureTitle = title,
            FeatureDescription = description
        });
    }

    private async Task<InsightsRevenueMarginViewModel> BuildSalesPageAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Sales";
        ViewData["Module"] = "Operations";
        ViewData["ModuleSubtitle"] = "Category, brand, channel, and top SKU breakdown from POS.";

        var (monthStart, _, periodLabel) = CurrentMonthPeriod();
        var hasSales = await _db.PosSales.AsNoTracking().AnyAsync(s => s.CompletedAtUtc >= monthStart, cancellationToken);

        var lineRows = await (
            from line in _db.PosSaleLines.AsNoTracking()
            join sale in _db.PosSales.AsNoTracking() on line.PosSaleId equals sale.Id
            join product in _db.CatalogProducts.AsNoTracking() on line.CatalogProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join brand in _db.CatalogBrands.AsNoTracking() on product!.BrandId equals brand.Id into brands
            from brand in brands.DefaultIfEmpty()
            where sale.CompletedAtUtc >= monthStart
            select new
            {
                line.LineTotal,
                line.LineCostTotal,
                line.Quantity,
                line.Sku,
                line.ProductName,
                Category = product != null ? product.Category : "Uncategorized",
                BrandName = brand != null ? brand.Name : "No brand"
            }).ToListAsync(cancellationToken);

        var byCategory = lineRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Uncategorized" : x.Category)
            .Select(g => new InsightsMarginRowViewModel
            {
                Label = g.Key,
                Revenue = g.Sum(x => x.LineTotal),
                Cost = g.Sum(x => x.LineCostTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var byBrand = lineRows
            .GroupBy(x => x.BrandName)
            .Select(g => new InsightsMarginRowViewModel
            {
                Label = g.Key,
                Revenue = g.Sum(x => x.LineTotal),
                Cost = g.Sum(x => x.LineCostTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var topProducts = lineRows
            .GroupBy(x => x.Sku)
            .Select(g => new InsightsTopProductRowViewModel
            {
                Sku = g.Key,
                Name = g.First().ProductName,
                Category = g.First().Category,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
                Margin = g.Sum(x => x.LineTotal - x.LineCostTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(15)
            .ToList();

        var posRevenue = await _db.PosSales.AsNoTracking()
            .Where(s => s.CompletedAtUtc >= monthStart)
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        var posCount = await _db.PosSales.AsNoTracking()
            .CountAsync(s => s.CompletedAtUtc >= monthStart, cancellationToken);

        var deliveredRepairsQuery = _db.RepairTickets.AsNoTracking()
            .Where(t => t.Status == RepairTicketStatus.Delivered && t.ModifiedAtUtc >= monthStart);

        var repairRevenue = await deliveredRepairsQuery.SumAsync(t => (decimal?)t.EstimatedPrice, cancellationToken) ?? 0m;
        var repairPartsCost = await deliveredRepairsQuery.SumAsync(t => (decimal?)t.PartsCost, cancellationToken) ?? 0m;
        var repairCount = await deliveredRepairsQuery.CountAsync(cancellationToken);

        var channels = new List<InsightsChannelRowViewModel>
        {
            new() { Channel = "POS", Revenue = posRevenue, TransactionCount = posCount, IsLive = true },
            new() { Channel = "Repairs", Revenue = repairRevenue, Cost = repairPartsCost, TransactionCount = repairCount, IsLive = true },
            new() { Channel = "Online store", Revenue = 0m, TransactionCount = 0, IsLive = false },
            new() { Channel = "Instagram orders", Revenue = 0m, TransactionCount = 0, IsLive = false },
            new() { Channel = "WhatsApp orders", Revenue = 0m, TransactionCount = 0, IsLive = false },
            new() { Channel = "Marketplace", Revenue = 0m, TransactionCount = 0, IsLive = false }
        };

        var totalRevenue = lineRows.Sum(x => x.LineTotal);
        var totalCost = lineRows.Sum(x => x.LineCostTotal);

        return new InsightsRevenueMarginViewModel
        {
            PeriodLabel = periodLabel,
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            RevenueByCategory = byCategory,
            MarginByCategory = byCategory,
            RevenueByBrand = byBrand,
            MarginByBrand = byBrand,
            RevenueByChannel = channels,
            TopProducts = topProducts,
            HasPosSales = hasSales
        };
    }
}
