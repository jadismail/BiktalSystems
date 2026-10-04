using System.Globalization;
using Biktal.Domain.Crm;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Sales)]
public sealed class CrmController : Controller
{
    private const int MaxLoyaltyPoints = 9_999_999;

    private readonly ApplicationDbContext _db;

    public CrmController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeCustomers = await _db.CrmCustomers.AsNoTracking()
            .CountAsync(c => c.IsActive, cancellationToken);

        var openTickets = await _db.CrmSupportTickets.AsNoTracking()
            .CountAsync(t => t.Status != CrmSupportTicketStatus.Resolved && t.Status != CrmSupportTicketStatus.Closed, cancellationToken);

        var activeCampaigns = await _db.CrmCampaigns.AsNoTracking()
            .CountAsync(c => c.Status == CrmCampaignStatus.Active, cancellationToken);

        var totalPoints = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .SumAsync(c => (int?)c.LoyaltyPoints, cancellationToken) ?? 0;

        var activePlans = await _db.CustomerInstallmentPlans.AsNoTracking()
            .Where(p => p.Status == InstallmentPlanStatus.Active)
            .Select(p => new { p.TotalAmount, p.AmountPaid })
            .ToListAsync(cancellationToken);

        return View(new CrmIndexViewModel
        {
            ActiveCustomerCount = activeCustomers,
            OpenSupportTicketCount = openTickets,
            ActiveCampaignCount = activeCampaigns,
            TotalLoyaltyPointsOutstanding = totalPoints,
            ActiveInstallmentPlanCount = activePlans.Count,
            InstallmentOutstanding = activePlans.Sum(p => p.TotalAmount - p.AmountPaid)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Customers(CancellationToken cancellationToken)
    {
        return View(await BuildCustomersPageAsync(cancellationToken));
    }

    [HttpGet]
    public IActionResult NewCustomer()
    {
        return View(new CreateCrmCustomerFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCustomer(CreateCrmCustomerFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("NewCustomer", model);

        _db.CrmCustomers.Add(new CrmCustomer
        {
            FullName = model.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            Company = string.IsNullOrWhiteSpace(model.Company) ? null : model.Company.Trim(),
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            LoyaltyPoints = 0,
            MarketingOptIn = model.MarketingOptIn,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["CrmMessage"] = "Customer created.";
        return RedirectToAction(nameof(Customers));
    }

    [HttpGet]
    public async Task<IActionResult> Customer(Guid id, CancellationToken cancellationToken)
    {
        var c = await _db.CrmCustomers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (c is null)
            return NotFound();

        var form = new EditCrmCustomerFormModel
        {
            Id = c.Id,
            FullName = c.FullName,
            Phone = c.Phone,
            Email = c.Email,
            Company = c.Company,
            Notes = c.Notes,
            MarketingOptIn = c.MarketingOptIn,
            LoyaltyPoints = c.LoyaltyPoints
        };

        return View(new CrmCustomerDetailPageViewModel { Form = form });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCustomer(EditCrmCustomerFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Customer", new CrmCustomerDetailPageViewModel { Form = model });

        var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);
        if (c is null)
            return NotFound();

        c.FullName = model.FullName.Trim();
        c.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        c.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        c.Company = string.IsNullOrWhiteSpace(model.Company) ? null : model.Company.Trim();
        c.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
        c.MarketingOptIn = model.MarketingOptIn;
        c.LoyaltyPoints = Math.Clamp(model.LoyaltyPoints, 0, MaxLoyaltyPoints);
        c.ModifiedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        TempData["CrmMessage"] = "Customer updated.";
        return RedirectToAction(nameof(Customer), new { id = model.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateCustomer(Guid id, CancellationToken cancellationToken)
    {
        var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null)
            return NotFound();

        c.IsActive = false;
        c.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CrmMessage"] = $"{c.FullName} is now inactive.";
        return RedirectToAction(nameof(Customers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateCustomer(Guid id, CancellationToken cancellationToken)
    {
        var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null)
            return NotFound();

        c.IsActive = true;
        c.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CrmMessage"] = $"{c.FullName} is active again.";
        return RedirectToAction(nameof(Customers));
    }

    [HttpGet]
    public async Task<IActionResult> Loyalty(CancellationToken cancellationToken)
    {
        return View(await BuildLoyaltyPageAsync(cancellationToken, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustLoyaltyPoints([Bind(Prefix = "AdjustForm")] AdjustLoyaltyPointsFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Loyalty", await BuildLoyaltyPageAsync(cancellationToken, model));

        var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == model.CustomerId && x.IsActive, cancellationToken);
        if (c is null)
        {
            ModelState.AddModelError(nameof(model.CustomerId), "Customer not found or inactive.");
            return View("Loyalty", await BuildLoyaltyPageAsync(cancellationToken, model));
        }

        var next = Math.Clamp(c.LoyaltyPoints + model.Delta, 0, MaxLoyaltyPoints);
        if (next == c.LoyaltyPoints && model.Delta != 0)
        {
            TempData["CrmError"] = model.Delta > 0
                ? $"Cannot exceed {MaxLoyaltyPoints:N0} points."
                : "Points cannot go below zero.";
            return RedirectToAction(nameof(Loyalty));
        }

        c.LoyaltyPoints = next;
        c.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CrmMessage"] =
            $"Adjusted {c.FullName} by {model.Delta.ToString("+0;-0", CultureInfo.InvariantCulture)} points (balance {next:N0}).";
        return RedirectToAction(nameof(Loyalty));
    }

    [HttpGet]
    public async Task<IActionResult> Campaigns(CancellationToken cancellationToken)
    {
        return View(await BuildCampaignsPageAsync(cancellationToken, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCampaign(
        [Bind(Prefix = "CreateForm")] CreateCrmCampaignFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Campaigns", await BuildCampaignsPageAsync(cancellationToken, model));

        _db.CrmCampaigns.Add(new CrmCampaign
        {
            Name = model.Name.Trim(),
            Channel = model.Channel.Trim(),
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            Status = model.Status,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["CrmMessage"] = "Campaign saved.";
        return RedirectToAction(nameof(Campaigns));
    }

    [HttpGet]
    public async Task<IActionResult> Tickets(CancellationToken cancellationToken)
    {
        return View(await BuildTicketsPageAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> NewTicket(CancellationToken cancellationToken)
    {
        return View(await BuildNewTicketPageAsync(new CreateSupportTicketFormModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTicket([Bind(Prefix = "Form")] CreateSupportTicketFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("NewTicket", await BuildNewTicketPageAsync(model, cancellationToken));

        var number = await AllocateSupportTicketNumberAsync(cancellationToken);
        var ticket = new CrmSupportTicket
        {
            TicketNumber = number,
            CustomerId = model.CustomerId is Guid g && g != Guid.Empty ? g : null,
            Subject = model.Subject.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            Status = CrmSupportTicketStatus.New,
            Priority = model.Priority,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.CrmSupportTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CrmMessage"] = $"Ticket {number} created.";
        return RedirectToAction(nameof(SupportTicket), new { id = ticket.Id });
    }

    [HttpGet]
    public async Task<IActionResult> SupportTicket(Guid id, CancellationToken cancellationToken)
    {
        var t = await _db.CrmSupportTickets.AsNoTracking()
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (t is null)
            return NotFound();

        var vm = new SupportTicketDetailViewModel
        {
            Id = t.Id,
            TicketNumber = t.TicketNumber,
            CustomerId = t.CustomerId,
            CustomerName = t.Customer?.FullName,
            Subject = t.Subject,
            Description = t.Description,
            Status = t.Status,
            Priority = t.Priority,
            CreatedAtUtc = t.CreatedAtUtc
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSupportTicket(Guid id, CrmSupportTicketStatus status, CrmSupportTicketPriority priority, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(CrmSupportTicketStatus), status) || !Enum.IsDefined(typeof(CrmSupportTicketPriority), priority))
        {
            TempData["CrmError"] = "Invalid status or priority.";
            return RedirectToAction(nameof(SupportTicket), new { id });
        }

        var t = await _db.CrmSupportTickets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null)
            return NotFound();

        t.Status = status;
        t.Priority = priority;
        t.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CrmMessage"] = "Ticket updated.";
        return RedirectToAction(nameof(SupportTicket), new { id });
    }

    private async Task<CrmCustomersPageViewModel> BuildCustomersPageAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.CrmCustomers.AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new CrmCustomerListRowViewModel
            {
                Id = c.Id,
                FullName = c.FullName,
                Phone = c.Phone,
                Email = c.Email,
                LoyaltyPoints = c.LoyaltyPoints,
                IsActive = c.IsActive
            })
            .ToListAsync(cancellationToken);

        return new CrmCustomersPageViewModel { Customers = rows };
    }

    private async Task<CrmLoyaltyPageViewModel> BuildLoyaltyPageAsync(CancellationToken cancellationToken, AdjustLoyaltyPointsFormModel? adjustForm)
    {
        var rankedData = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.LoyaltyPoints)
            .ThenBy(c => c.FullName)
            .Select(c => new { c.Id, c.FullName, c.LoyaltyPoints })
            .ToListAsync(cancellationToken);

        var ranked = rankedData.Select((c, i) => new CrmLoyaltyRankRowViewModel
        {
            Rank = i + 1,
            CustomerId = c.Id,
            FullName = c.FullName,
            LoyaltyPoints = c.LoyaltyPoints
        }).ToList();

        var options = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.FullName)
            .Select(c => new CrmCustomerOptionViewModel
            {
                Id = c.Id,
                Label = c.FullName + (c.Phone != null ? " · " + c.Phone : "")
            })
            .ToListAsync(cancellationToken);

        return new CrmLoyaltyPageViewModel
        {
            Ranked = ranked,
            CustomerOptions = options,
            AdjustForm = adjustForm ?? new AdjustLoyaltyPointsFormModel()
        };
    }

    private async Task<CrmCampaignsPageViewModel> BuildCampaignsPageAsync(CancellationToken cancellationToken, CreateCrmCampaignFormModel? form)
    {
        var rows = await _db.CrmCampaigns.AsNoTracking()
            .OrderByDescending(c => c.StartDate)
            .ThenByDescending(c => c.CreatedAtUtc)
            .Select(c => new CrmCampaignRowViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Channel = c.Channel,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Status = c.Status
            })
            .ToListAsync(cancellationToken);

        return new CrmCampaignsPageViewModel
        {
            Campaigns = rows,
            CreateForm = form ?? new CreateCrmCampaignFormModel()
        };
    }

    private async Task<CrmTicketsPageViewModel> BuildTicketsPageAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.CrmSupportTickets.AsNoTracking()
            .Include(t => t.Customer)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(150)
            .Select(t => new CrmSupportTicketListRowViewModel
            {
                Id = t.Id,
                TicketNumber = t.TicketNumber,
                CustomerName = t.Customer != null ? t.Customer.FullName : null,
                Subject = t.Subject,
                Status = t.Status,
                Priority = t.Priority
            })
            .ToListAsync(cancellationToken);

        return new CrmTicketsPageViewModel { Tickets = rows };
    }

    private async Task<NewTicketPageViewModel> BuildNewTicketPageAsync(CreateSupportTicketFormModel form, CancellationToken cancellationToken)
    {
        var options = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.FullName)
            .Select(c => new CrmCustomerOptionViewModel
            {
                Id = c.Id,
                Label = c.FullName
            })
            .ToListAsync(cancellationToken);

        return new NewTicketPageViewModel { Form = form, CustomerOptions = options };
    }

    private async Task<string> AllocateSupportTicketNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = $"ST-{DateTime.UtcNow:yyyyMMdd}-";

        var last = await _db.CrmSupportTickets.AsNoTracking()
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
            throw new InvalidOperationException("Daily support ticket sequence exhausted.");

        return $"{prefix}{next:D4}";
    }
}
