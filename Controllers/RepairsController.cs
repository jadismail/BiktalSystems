using System.Globalization;
using System.Security.Cryptography;
using Biktal.Domain.Inventory;
using Biktal.Domain.Repairs;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Floor)]
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
    private readonly RepairDeliveryPostingService _repairDelivery;

    public RepairsController(ApplicationDbContext db, RepairDeliveryPostingService repairDelivery)
    {
        _db = db;
        _repairDelivery = repairDelivery;
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
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        return View(new NewRepairTicketFormModel
        {
            Barcode = await AllocateRepairBarcodeAsync(cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> NextBarcode(CancellationToken cancellationToken)
    {
        var barcode = await AllocateRepairBarcodeAsync(cancellationToken);
        return Json(new { barcode });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTicket(NewRepairTicketFormModel model, CancellationToken cancellationToken)
    {
        var barcode = (model.Barcode ?? string.Empty).Trim().ToUpperInvariant();
        model.Barcode = barcode;

        if (string.IsNullOrWhiteSpace(barcode))
            ModelState.AddModelError(nameof(model.Barcode), "Generate a device barcode before creating the ticket.");
        else if (await _db.RepairTickets.AsNoTracking()
                     .AnyAsync(t => t.Barcode == barcode, cancellationToken))
            ModelState.AddModelError(nameof(model.Barcode), "That barcode is already used. Generate a new one.");

        if (!ModelState.IsValid)
        {
            if (string.IsNullOrWhiteSpace(model.Barcode))
                model.Barcode = await AllocateRepairBarcodeAsync(cancellationToken);
            return View("New", model);
        }

        var ticketNumber = await AllocateRepairTicketNumberAsync(cancellationToken);

        var ticket = new RepairTicket
        {
            TicketNumber = ticketNumber,
            Barcode = barcode,
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

        TempData["RepairMessage"] = $"Repair ticket {ticket.TicketNumber} created. Print the barcode label and place it on the device.";
        return RedirectToAction(nameof(Ticket), new { id = ticket.Id, printBarcode = 1 });
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
            Barcode = ticket.Barcode,
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
            AmountPaidCash = ticket.AmountPaidCash,
            AmountPaidWhish = ticket.AmountPaidWhish,
            IsPostedToAccounts = ticket.JournalEntryId is not null,
            CreatedAtUtc = ticket.CreatedAtUtc,
            CanDelete = ticket.Status == RepairTicketStatus.Intake || ticket.Status == RepairTicketStatus.Cancelled
        };

        ViewData["PrintBarcode"] = string.Equals(Request.Query["printBarcode"], "1", StringComparison.Ordinal);
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

        if (status == RepairTicketStatus.Delivered)
        {
            TempData["RepairError"] = "Use Delivered with Cash and Whish amounts from Pricing & margin.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var ticket = await _db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        if (ticket.JournalEntryId is not null && ticket.Status == RepairTicketStatus.Delivered)
        {
            TempData["RepairError"] = "This repair was already posted to Accounts and cannot change status.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        ticket.Status = status;
        ticket.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["RepairMessage"] = $"Status updated to {RepairTicketStatusLabels.Title(status)}.";
        return RedirectToAction(nameof(Ticket), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeliverTicket(
        Guid id,
        string? amountPaidCash,
        string? amountPaidWhish,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalMoney(amountPaidCash, out var cash, out var cashError))
        {
            TempData["RepairError"] = cashError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        if (!TryParseOptionalMoney(amountPaidWhish, out var whish, out var whishError))
        {
            TempData["RepairError"] = whishError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var result = await _repairDelivery.DeliverAsync(
            id,
            cash ?? 0m,
            whish ?? 0m,
            cancellationToken);

        if (!result.Success)
        {
            TempData["RepairError"] = result.Error;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var money = CultureInfo.GetCultureInfo("en-US");
        var sessionNote = result.LinkedToCashboxSession
            ? "Posted to the open Accounts session."
            : "Posted to the ledger (no open Accounts session).";
        TempData["RepairMessage"] =
            $"Delivered — Cash {result.AmountPaidCash.ToString("C", money)}, Whish {result.AmountPaidWhish.ToString("C", money)}. {sessionNote}";

        return RedirectToAction(nameof(Ticket), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTicketEstimate(
        Guid id,
        string? estimatedPrice,
        string? partsCost,
        string? amountPaidCash,
        string? amountPaidWhish,
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

        if (!TryParseOptionalMoney(amountPaidCash, out var cash, out var cashError))
        {
            TempData["RepairError"] = cashError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        if (!TryParseOptionalMoney(amountPaidWhish, out var whish, out var whishError))
        {
            TempData["RepairError"] = whishError!;
            return RedirectToAction(nameof(Ticket), new { id });
        }

        if (price is decimal p && cost is decimal c && c > p)
        {
            TempData["RepairError"] = "Parts cost cannot exceed the customer quote.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var paidCash = cash ?? 0m;
        var paidWhish = whish ?? 0m;
        if (price is decimal quote && quote > 0m && paidCash + paidWhish > quote + 0.01m)
        {
            TempData["RepairError"] = "Cash + Whish cannot exceed the customer quote.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        var ticket = await _db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        if (ticket.JournalEntryId is not null)
        {
            TempData["RepairError"] = "Pricing is locked after the repair was posted to Accounts.";
            return RedirectToAction(nameof(Ticket), new { id });
        }

        ticket.EstimatedPrice = price;
        ticket.PartsCost = cost;
        ticket.AmountPaidCash = paidCash;
        ticket.AmountPaidWhish = paidWhish;
        ticket.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var money = CultureInfo.GetCultureInfo("en-US");
        TempData["RepairMessage"] = price is null && cost is null && paidCash == 0m && paidWhish == 0m
            ? "Quote, parts cost, and payments cleared."
            : $"Pricing updated — quote {(price?.ToString("C", money) ?? "—")}, cash {paidCash.ToString("C", money)}, Whish {paidWhish.ToString("C", money)}.";

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
            Barcode = t.Barcode,
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

    private async Task<string> AllocateRepairBarcodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var candidate = GenerateRepairBarcode();
            var exists = await _db.RepairTickets.AsNoTracking()
                .AnyAsync(t => t.Barcode == candidate, cancellationToken);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException("Could not allocate a unique repair barcode.");
    }

    private static string GenerateRepairBarcode()
    {
        Span<byte> bytes = stackalloc byte[5];
        RandomNumberGenerator.Fill(bytes);
        var n = ((ulong)bytes[0] << 32)
                | ((ulong)bytes[1] << 24)
                | ((ulong)bytes[2] << 16)
                | ((ulong)bytes[3] << 8)
                | bytes[4];
        // Compact Code128-friendly value, e.g. BK4829173056
        return $"BK{(n % 10_000_000_000UL):D10}";
    }
}
