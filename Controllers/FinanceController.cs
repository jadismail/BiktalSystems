using Biktal.Domain.Finance;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Finance)]
public sealed class FinanceController : Controller
{
    private readonly ApplicationDbContext _db;

    public FinanceController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult Accounts() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult Journal() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult NewJournal() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult JournalDetail(Guid id)
    {
        _ = id;
        return RedirectToAction(nameof(Expenses));
    }

    [HttpGet]
    public IActionResult Accounting() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult Aging() => RedirectToAction(nameof(Expenses));

    [HttpGet]
    public IActionResult Cashbox() =>
        RedirectToAction(nameof(AccountsController.Index), "Accounts");

    [HttpGet]
    public async Task<IActionResult> Expenses(CancellationToken cancellationToken)
    {
        return View(await BuildExpensesPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateExpense(
        [Bind(Prefix = "CreateForm")] CreateFinanceExpenseFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Expenses", await BuildExpensesPageAsync(cancellationToken, model));

        _db.FinanceExpenses.Add(new FinanceExpense
        {
            ExpenseDate = model.ExpenseDate,
            VendorName = model.VendorName.Trim(),
            Category = model.Category.Trim(),
            Amount = model.Amount,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["FinanceMessage"] = "Expense logged.";
        return RedirectToAction(nameof(Expenses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.FinanceExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        _db.FinanceExpenses.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["FinanceMessage"] = "Expense removed.";
        return RedirectToAction(nameof(Expenses));
    }

    private async Task<FinanceExpensesPageViewModel> BuildExpensesPageAsync(CancellationToken cancellationToken,
        CreateFinanceExpenseFormModel? createForm = null)
    {
        var rows = await _db.FinanceExpenses.AsNoTracking()
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => new FinanceExpenseRowViewModel
            {
                Id = e.Id,
                ExpenseDate = e.ExpenseDate,
                VendorName = e.VendorName,
                Category = e.Category,
                Amount = e.Amount,
                Notes = e.Notes
            })
            .ToListAsync(cancellationToken);

        return new FinanceExpensesPageViewModel
        {
            Expenses = rows,
            CreateForm = createForm ?? new CreateFinanceExpenseFormModel()
        };
    }
}
