using Biktal.Domain.Crm;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class InstallmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly InstallmentPlanService _installments;

    public InstallmentsController(ApplicationDbContext db, InstallmentPlanService installments)
    {
        _db = db;
        _installments = installments;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Installments";
        ViewData["Module"] = "CRM";
        ViewData["ModuleSubtitle"] = "Customer payment plans, schedules, and balances owed.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var plans = await _db.CustomerInstallmentPlans.AsNoTracking()
            .Include(p => p.CrmCustomer)
            .Include(p => p.Schedule)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
            InstallmentPlanService.RefreshScheduleStatuses(plan, today);

        var rows = plans.Select(p =>
        {
            var nextDue = p.Schedule
                .Where(s => s.Status is not InstallmentScheduleItemStatus.Paid)
                .OrderBy(s => s.DueDate)
                .Select(s => (DateOnly?)s.DueDate)
                .FirstOrDefault();

            return new InstallmentPlanListRowViewModel
            {
                Id = p.Id,
                PlanNumber = p.PlanNumber,
                CustomerName = p.CrmCustomer.FullName,
                Description = p.Description,
                TotalAmount = p.TotalAmount,
                BalanceDue = InstallmentPlanService.RoundMoney(p.TotalAmount - p.AmountPaid),
                Status = p.Status,
                NextDueDate = nextDue,
                CreatedAtUtc = p.CreatedAtUtc
            };
        }).ToList();

        var active = rows.Where(r => r.Status == InstallmentPlanStatus.Active).ToList();

        return View(new InstallmentsIndexViewModel
        {
            TotalOutstanding = active.Sum(r => r.BalanceDue),
            ActivePlanCount = active.Count,
            OverdueInstallmentCount = plans
                .SelectMany(p => p.Schedule)
                .Count(s => s.Status == InstallmentScheduleItemStatus.Overdue),
            Plans = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> New(Guid? customerId, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New installment plan";
        ViewData["Module"] = "CRM";
        ViewData["ModuleSubtitle"] = "Set total, down payment, and monthly schedule.";

        var form = new CreateInstallmentPlanFormModel();
        if (customerId is { } cid && cid != Guid.Empty)
            form.CrmCustomerId = cid;

        return View(await BuildNewInstallmentPageAsync(form, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind(Prefix = nameof(NewInstallmentPageViewModel.Form))] CreateInstallmentPlanFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New installment plan";
        ViewData["Module"] = "CRM";
        ViewData["ModuleSubtitle"] = "Set total, down payment, and monthly schedule.";

        if (!ModelState.IsValid)
            return View("New", await BuildNewInstallmentPageAsync(model, cancellationToken));

        var result = await _installments.CreatePlanAsync(new CreateInstallmentPlanCommand
        {
            CrmCustomerId = model.CrmCustomerId,
            Description = model.Description,
            TotalAmount = model.TotalAmount,
            DownPayment = model.DownPayment,
            InstallmentCount = model.InstallmentCount,
            FirstDueDate = model.FirstDueDate,
            DownPaymentMethod = model.DownPaymentMethod,
            Notes = model.Notes
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create plan.");
            return View("New", await BuildNewInstallmentPageAsync(model, cancellationToken));
        }

        TempData["CrmMessage"] = $"Installment plan {result.PlanNumber} was created.";
        return RedirectToAction(nameof(Detail), new { id = result.PlanId });
    }

    [HttpGet]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var plan = await _db.CustomerInstallmentPlans.AsNoTracking()
            .Include(p => p.CrmCustomer)
            .Include(p => p.Schedule)
            .Include(p => p.Payments)
            .Include(p => p.OpeningJournalEntry)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan is null)
            return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        InstallmentPlanService.RefreshScheduleStatuses(plan, today);

        ViewData["Title"] = plan.PlanNumber;
        ViewData["Module"] = "CRM";
        ViewData["ModuleSubtitle"] = plan.Description;

        var scheduleSeq = plan.Schedule.ToDictionary(s => s.Id, s => s.Sequence);

        return View(new InstallmentPlanDetailViewModel
        {
            Id = plan.Id,
            PlanNumber = plan.PlanNumber,
            CustomerId = plan.CrmCustomerId,
            CustomerName = plan.CrmCustomer.FullName,
            CustomerPhone = plan.CrmCustomer.Phone,
            Description = plan.Description,
            TotalAmount = plan.TotalAmount,
            DownPayment = plan.DownPayment,
            FinancedAmount = plan.FinancedAmount,
            AmountPaid = plan.AmountPaid,
            BalanceDue = InstallmentPlanService.RoundMoney(plan.TotalAmount - plan.AmountPaid),
            Status = plan.Status,
            StartDate = plan.StartDate,
            Notes = plan.Notes,
            OpeningJournalReference = plan.OpeningJournalEntry?.Reference,
            Schedule = plan.Schedule
                .OrderBy(s => s.Sequence)
                .Select(s => new InstallmentScheduleRowViewModel
                {
                    Sequence = s.Sequence,
                    DueDate = s.DueDate,
                    AmountDue = s.AmountDue,
                    AmountPaid = s.AmountPaid,
                    Remaining = InstallmentPlanService.RoundMoney(s.AmountDue - s.AmountPaid),
                    Status = s.Status
                })
                .ToList(),
            Payments = plan.Payments
                .OrderByDescending(p => p.ReceivedAtUtc)
                .Select(p => new InstallmentPaymentRowViewModel
                {
                    ReceivedAtUtc = p.ReceivedAtUtc,
                    Amount = p.Amount,
                    Method = p.Method,
                    Notes = p.Notes,
                    ScheduleSequence = p.ScheduleItemId is { } sid && scheduleSeq.TryGetValue(sid, out var seq) ? seq : null
                })
                .ToList(),
            PaymentForm = new RecordInstallmentPaymentFormModel { PlanId = plan.Id }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(
        [Bind(Prefix = nameof(InstallmentPlanDetailViewModel.PaymentForm))] RecordInstallmentPaymentFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["CrmError"] = "Enter a valid payment amount.";
            return RedirectToAction(nameof(Detail), new { id = model.PlanId });
        }

        var result = await _installments.RecordPaymentAsync(new RecordInstallmentPaymentCommand
        {
            PlanId = model.PlanId,
            Amount = model.Amount,
            Method = model.Method,
            Notes = model.Notes
        }, cancellationToken);

        if (!result.Success)
        {
            TempData["CrmError"] = result.Error;
            return RedirectToAction(nameof(Detail), new { id = model.PlanId });
        }

        TempData["CrmMessage"] = $"Payment of {model.Amount:C} recorded on {result.PlanNumber}.";
        return RedirectToAction(nameof(Detail), new { id = model.PlanId });
    }

    private async Task<NewInstallmentPageViewModel> BuildNewInstallmentPageAsync(
        CreateInstallmentPlanFormModel form,
        CancellationToken cancellationToken)
    {
        var customers = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.FullName)
            .Select(c => new CrmCustomerOptionViewModel
            {
                Id = c.Id,
                Label = c.FullName + (c.Phone != null ? $" · {c.Phone}" : "")
            })
            .ToListAsync(cancellationToken);

        return new NewInstallmentPageViewModel
        {
            Customers = customers,
            Form = form
        };
    }
}
