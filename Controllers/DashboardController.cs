using Biktal.Domain.Finance;
using Biktal.Domain.Repairs;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-page-dashboard";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var revenueToday = await (
                from line in _db.JournalEntryLines.AsNoTracking()
                join je in _db.JournalEntries.AsNoTracking() on line.JournalEntryId equals je.Id
                join acc in _db.GeneralLedgerAccounts.AsNoTracking() on line.GeneralLedgerAccountId equals acc.Id
                where je.EntryDate == today && acc.Type == GlAccountType.Revenue
                select line.CreditAmount - line.DebitAmount)
            .SumAsync(cancellationToken);

        var repairsOpen = await _db.RepairTickets.AsNoTracking()
            .CountAsync(t => t.Status != RepairTicketStatus.Delivered && t.Status != RepairTicketStatus.Cancelled,
                cancellationToken);

        var lowStock = await _db.CatalogProducts.AsNoTracking()
            .CountAsync(p => p.StockQuantity <= DashboardKpiViewModel.LowStockThreshold, cancellationToken);

        return View(new DashboardKpiViewModel
        {
            TodayRevenueFromJournals = revenueToday,
            RepairsInProgress = repairsOpen,
            LowStockSkus = lowStock
        });
    }
}
