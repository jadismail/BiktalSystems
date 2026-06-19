using System.Globalization;
using Biktal.Domain.Inventory;
using Biktal.Domain.Repairs;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class RepairsController : Controller
{
    private const decimal MaxRepairEstimate = 999_999.99m;

    private static readonly RepairTicketStatus[] BoardStatuses =
    [
        RepairTicketStatus.Intake,
        RepairTicketStatus.Inspection,
        RepairTicketStatus.WaitingParts,
        RepairTicketStatus.InRepair,
        RepairTicketStatus.ReadyForPickup
    ];

    private readonly ApplicationDbContext _db;

    public RepairsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var openRepairs = await _db.RepairTickets.AsNoTracking()
            .CountAsync(t => t.Status != RepairTicketStatus.Delivered && t.Status != RepairTicketStatus.Cancelled, cancellationToken);

        var delivered = await _db.RepairTickets.AsNoTracking()
            .CountAsync(t => t.Status == RepairTicketStatus.Delivered, cancellationToken);

        var openPos = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(po => po.Status != PurchaseOrderStatus.Received && po.Status != PurchaseOrderStatus.Cancelled, cancellationToken);

        return View(new RepairsIndexViewModel
        {
            OpenRepairsCount = openRepairs,
            DeliveredTicketsCount = delivered,
            OpenPurchaseOrdersCount = openPos
        });
    }

    [HttpGet]
    public async Task<IActionResult> Tickets(CancellationToken cancellationToken)
    {
        return View(await BuildRepairTicketsPageAsync(cancellationToken));
    }

    [HttpGet]
    public IActionResult New()
    {
        return View(new NewRepairTicketFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTicket(NewRepairTicketFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("New", model);

        var ticketNumber = await AllocateRepairTicketNumberAsync(cancellationToken);

        var ticket = new RepairTicket
        {
            TicketNumber = ticketNumber,
            CustomerName = model.CustomerName.Trim(),
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            DeviceSummary = model.DeviceSummary.Trim(),
            SerialOrImei = string.IsNullOrWhiteSpace(model.SerialOrImei) ? null : model.SerialOrImei.Trim(),
            IssueDescription = model.IssueDescription.Trim(),
            AccessoriesNotes = string.IsNullOrWhiteSpace(model.AccessoriesNotes) ? null : model.AccessoriesNotes.Trim(),
            EstimatedPrice = model.EstimatedPrice,
            PartsCost = model.PartsCost,
            Status = RepairTicketStatus.Intake,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.RepairTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["RepairMessage"] = $"Repair ticket {ticket.TicketNumber} created.";
        return RedirectToAction(nameof(Ticket), new { id = ticket.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Ticket(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _db.RepairTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (ticket is null)
            return NotFound();

        var vm = new RepairTicketDetailViewModel
        {
            Id = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            Status = ticket.Status,
            CustomerName = ticket.CustomerName,
            Phone = ticket.Phone,
            Email = ticket.Email,
            DeviceSummary = ticket.DeviceSummary,
            SerialOrImei = ticket.SerialOrImei,
            IssueDescription = ticket.IssueDescription,
            AccessoriesNotes = ticket.AccessoriesNotes,
            EstimatedPrice = ticket.EstimatedPrice,
            PartsCost = ticket.PartsCost,
            CreatedAtUtc = ticket.CreatedAtUtc,
            CanDelete = ticket.Status == RepairTicketStatus.Intake || ticket.Status == RepairTicketStatus.Cancelled
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTicketStatus(Guid id, RepairTicketStatus status, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(RepairTicketStatus), status))
        {
            TempData["RepairError"] = "Invalid status.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var ticket = await _db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        ticket.Status = status;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["RepairMessage"] = $"Status updated to {RepairTicketStatusLabels.Title(status)}.";
        return RedirectToAction(nameof(Ticket), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTicketEstimate(Guid id, string? estimatedPrice, string? partsCost,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalMoney(estimatedPrice, out var price, out var priceError))
        {
            TempData["RepairError"] = priceError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        if (!TryParseOptionalMoney(partsCost, out var cost, out var costError))
        {
            TempData["RepairError"] = costError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        if (price is decimal p && cost is decimal c && c > p)
        {
            TempData["RepairError"] = "Parts cost cannot exceed the customer quote.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var ticket = await _db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        ticket.EstimatedPrice = price;
        ticket.PartsCost = cost;
        ticket.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var money = CultureInfo.GetCultureInfo("en-US");
        TempData["RepairMessage"] = price is null && cost is null
            ? "Quote and parts cost cleared."
            : $"Pricing updated — quote {(price?.ToString("C", money) ?? "—")}, parts {(cost?.ToString("C", money) ?? "—")}.";

        return RedirectToAction(nameof(Ticket), new { id });
    }

    private static bool TryParseOptionalMoney(string? rawInput, out decimal? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawInput))
            return true;

        var raw = rawInput.Trim();
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            error = "Enter a valid amount, or leave the field blank to clear.";
            return false;
        }

        if (parsed < 0m || parsed > MaxRepairEstimate)
        {
            error = $"Amount must be between 0 and {MaxRepairEstimate:N2}.";
            return false;
        }

        value = parsed;
        return true;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRepairTicket(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        if (ticket.Status != RepairTicketStatus.Intake && ticket.Status != RepairTicketStatus.Cancelled)
        {
            TempData["RepairError"] = "Only intake or cancelled tickets can be deleted.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        _db.RepairTickets.Remove(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["RepairMessage"] = $"Ticket {ticket.TicketNumber} removed.";
        return RedirectToAction(nameof(Tickets));
    }

    private async Task<RepairTicketsPageViewModel> BuildRepairTicketsPageAsync(CancellationToken cancellationToken)
    {
        var list = await _db.RepairTickets.AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        static RepairTicketCardViewModel Map(RepairTicket t) => new()
        {
            Id = t.Id,
            TicketNumber = t.TicketNumber,
            CustomerName = t.CustomerName,
            DeviceSummary = t.DeviceSummary,
            Status = t.Status
        };

        var columns = BoardStatuses.Select(st => new RepairTicketColumnViewModel
        {
            Status = st,
            Title = RepairTicketStatusLabels.Title(st),
            Tickets = list.Where(t => t.Status == st).Select(Map).ToList()
        }).ToList();

        var closed = list
            .Where(t => t.Status == RepairTicketStatus.Delivered || t.Status == RepairTicketStatus.Cancelled)
            .Select(Map)
            .ToList();

        return new RepairTicketsPageViewModel
        {
            Columns = columns,
            ClosedTickets = closed
        };
    }

    private async Task<string> AllocateRepairTicketNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = $"RT-{DateTime.UtcNow:yyyyMMdd}-";

        var last = await _db.RepairTickets.AsNoTracking()
            .Where(t => t.TicketNumber.StartsWith(prefix))
            .OrderByDescending(t => t.TicketNumber)
            .Select(t => t.TicketNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(last) && last.Length >= prefix.Length + 4)
        {
            var suffix = last.AsSpan(prefix.Length);
            if (suffix.Length == 4 && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                next = n + 1;
        }

        if (next > 9999)
            throw new InvalidOperationException("Daily repair ticket sequence exhausted.");

        return $"{prefix}{next:D4}";
    }
}
