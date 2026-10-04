using System.Globalization;
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
    private const decimal MaxJournalLineAmount = 999_999.99m;

    private sealed record ParsedJournalLine(Guid AccountId, decimal Debit, decimal Credit, string? LineMemo);

    private readonly ApplicationDbContext _db;

    public FinanceController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        var activeAccounts = await _db.GeneralLedgerAccounts.AsNoTracking()
            .CountAsync(a => a.IsActive, cancellationToken);

        var journalCount = await _db.JournalEntries.AsNoTracking()
            .CountAsync(cancellationToken);

        var journalMonth = await _db.JournalEntries.AsNoTracking()
            .CountAsync(j => j.EntryDate >= firstOfMonth && j.EntryDate <= today, cancellationToken);

        var expensesMonth = await _db.FinanceExpenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= firstOfMonth && e.ExpenseDate <= today)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        return View(new FinanceIndexViewModel
        {
            ActiveAccountCount = activeAccounts,
            JournalEntryCount = journalCount,
            JournalEntriesThisMonth = journalMonth,
            ExpensesThisMonthTotal = expensesMonth
        });
    }

    [HttpGet]
    public async Task<IActionResult> Accounts(CancellationToken cancellationToken)
    {
        return View(await BuildAccountsPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(
        [Bind(Prefix = "CreateForm")] CreateGlAccountFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Accounts", await BuildAccountsPageAsync(cancellationToken, model));

        var code = model.AccountCode.Trim().ToUpperInvariant();
        var name = model.Name.Trim();

        var exists = await _db.GeneralLedgerAccounts.AsNoTracking()
            .AnyAsync(a => a.AccountCode.ToUpper() == code, cancellationToken);

        if (exists)
        {
            return View("Accounts", await BuildAccountsPageAsync(cancellationToken, model));
        }

        _db.GeneralLedgerAccounts.Add(new GeneralLedgerAccount
        {
            AccountCode = code,
            Name = name,
            Type = model.Type,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["FinanceMessage"] = $"Account {code} was added.";
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateAccount(Guid id, CancellationToken cancellationToken)
    {
        var account = await _db.GeneralLedgerAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
            return NotFound();

        account.IsActive = false;
        account.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["FinanceMessage"] = $"Account {account.AccountCode} is now inactive.";
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateAccount(Guid id, CancellationToken cancellationToken)
    {
        var account = await _db.GeneralLedgerAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
            return NotFound();

        account.IsActive = true;
        account.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["FinanceMessage"] = $"Account {account.AccountCode} is active again.";
        return RedirectToAction(nameof(Accounts));
    }

    [HttpGet]
    public async Task<IActionResult> Journal(CancellationToken cancellationToken)
    {
        var entries = await _db.JournalEntries.AsNoTracking()
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Take(100)
            .Select(e => new JournalListRowViewModel
            {
                Id = e.Id,
                EntryDate = e.EntryDate,
                Reference = e.Reference,
                Memo = e.Memo,
                TotalDebits = e.Lines.Sum(l => l.DebitAmount)
            })
            .ToListAsync(cancellationToken);

        return View(new JournalListPageViewModel { Entries = entries });
    }

    [HttpGet]
    public async Task<IActionResult> NewJournal(CancellationToken cancellationToken)
    {
        return View(await BuildNewJournalPageAsync(new NewJournalEntryFormModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateJournal([Bind(Prefix = "Form")] NewJournalEntryFormModel model, CancellationToken cancellationToken)
    {
        PadJournalLines(model);

        if (!ModelState.IsValid)
            return View("NewJournal", await BuildNewJournalPageAsync(model, cancellationToken));

        var lineInputs = ParseJournalLines(model);
        if (lineInputs.Count < 2)
        {
            ModelState.AddModelError(string.Empty, "Add at least two lines with an account and either a debit or a credit amount.");
            return View("NewJournal", await BuildNewJournalPageAsync(model, cancellationToken));
        }

        var totalDebit = lineInputs.Sum(l => l.Debit);
        var totalCredit = lineInputs.Sum(l => l.Credit);
        if (totalDebit != totalCredit)
        {
            ModelState.AddModelError(string.Empty,
                $"Debits ({totalDebit:N2}) must equal credits ({totalCredit:N2}) before posting.");
            return View("NewJournal", await BuildNewJournalPageAsync(model, cancellationToken));
        }

        if (totalDebit <= 0m)
        {
            ModelState.AddModelError(string.Empty, "Total debits (and credits) must be greater than zero.");
            return View("NewJournal", await BuildNewJournalPageAsync(model, cancellationToken));
        }

        var accountIds = lineInputs.Select(l => l.AccountId).Distinct().ToList();
        var activeCount = await _db.GeneralLedgerAccounts.AsNoTracking()
            .CountAsync(a => accountIds.Contains(a.Id) && a.IsActive, cancellationToken);

        if (activeCount != accountIds.Count)
        {
            ModelState.AddModelError(string.Empty, "Each line must use an active chart of accounts code.");
            return View("NewJournal", await BuildNewJournalPageAsync(model, cancellationToken));
        }

        var reference = await AllocateJournalReferenceAsync(cancellationToken);
        var journalId = Guid.NewGuid();

        var entry = new JournalEntry
        {
            Id = journalId,
            EntryDate = model.EntryDate,
            Reference = reference,
            Memo = string.IsNullOrWhiteSpace(model.Memo) ? null : model.Memo.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Lines = new List<JournalEntryLine>()
        };

        var order = 0;
        foreach (var line in lineInputs)
        {
            entry.Lines.Add(new JournalEntryLine
            {
                Id = Guid.NewGuid(),
                JournalEntryId = journalId,
                GeneralLedgerAccountId = line.AccountId,
                SortOrder = order++,
                LineMemo = line.LineMemo,
                DebitAmount = line.Debit,
                CreditAmount = line.Credit
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["FinanceMessage"] = $"Journal {reference} posted.";
        return RedirectToAction(nameof(JournalDetail), new { id = journalId });
    }

    [HttpGet]
    public async Task<IActionResult> JournalDetail(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _db.JournalEntries.AsNoTracking()
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry is null)
            return NotFound();

        var lines = entry.Lines
            .OrderBy(l => l.SortOrder)
            .Select(l => new JournalLineDetailViewModel
            {
                AccountCode = l.Account?.AccountCode ?? "—",
                AccountName = l.Account?.Name ?? "(removed)",
                LineMemo = l.LineMemo,
                Debit = l.DebitAmount,
                Credit = l.CreditAmount
            })
            .ToList();

        var vm = new JournalEntryDetailViewModel
        {
            Id = entry.Id,
            EntryDate = entry.EntryDate,
            Reference = entry.Reference,
            Memo = entry.Memo,
            CreatedAtUtc = entry.CreatedAtUtc,
            Lines = lines,
            TotalDebits = lines.Sum(l => l.Debit),
            TotalCredits = lines.Sum(l => l.Credit)
        };

        return View(vm);
    }

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

    [HttpGet]
    public async Task<IActionResult> Accounting(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Accounting";
        ViewData["Module"] = "Finance";
        ViewData["ModuleSubtitle"] = "Premium ERP — P&L, balance sheet, trial balance, journals, tax, and expenses.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var activeAccounts = await _db.GeneralLedgerAccounts.AsNoTracking()
            .CountAsync(a => a.IsActive, cancellationToken);

        var journalCount = await _db.JournalEntries.AsNoTracking().CountAsync(cancellationToken);
        var journalMonth = await _db.JournalEntries.AsNoTracking()
            .CountAsync(j => j.EntryDate >= monthStart && j.EntryDate <= today, cancellationToken);

        var expensesMonth = await _db.FinanceExpenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= monthStart && e.ExpenseDate <= today)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var byType = await _db.GeneralLedgerAccounts.AsNoTracking()
            .GroupBy(a => a.Type)
            .Select(g => new ReportsGlAccountTypeCountViewModel { Type = g.Key, Count = g.Count() })
            .OrderBy(x => x.Type)
            .ToListAsync(cancellationToken);

        var aggregateList = await _db.JournalEntryLines.AsNoTracking()
            .GroupBy(l => l.GeneralLedgerAccountId)
            .Select(g => new { Id = g.Key, Debits = g.Sum(l => l.DebitAmount), Credits = g.Sum(l => l.CreditAmount) })
            .ToListAsync(cancellationToken);

        var aggregates = aggregateList.ToDictionary(x => x.Id, x => (x.Debits, x.Credits));

        var accounts = await _db.GeneralLedgerAccounts.AsNoTracking()
            .OrderBy(a => a.AccountCode)
            .ToListAsync(cancellationToken);

        var trialRows = accounts
            .Select(a =>
            {
                decimal debits = 0m;
                decimal credits = 0m;
                if (aggregates.TryGetValue(a.Id, out var pair))
                {
                    debits = pair.Debits;
                    credits = pair.Credits;
                }

                return new ReportsTrialBalanceRowViewModel
                {
                    AccountCode = a.AccountCode,
                    Name = a.Name,
                    Type = a.Type,
                    Balance = PostedBalance(a.Type, debits, credits)
                };
            })
            .OrderByDescending(r => Math.Abs(r.Balance))
            .Take(35)
            .ToList();

        return View(new ReportsFinancialPageViewModel
        {
            ActiveGlAccountCount = activeAccounts,
            JournalEntryCount = journalCount,
            JournalEntriesThisMonth = journalMonth,
            ExpensesThisMonth = expensesMonth,
            AccountsByType = byType,
            TrialBalancePreview = trialRows
        });
    }

    [HttpGet]
    public IActionResult Cashbox() =>
        RedirectToAction(nameof(AccountsController.Index), "Accounts");

    [HttpGet]
    public IActionResult Aging()
    {
        ViewData["Title"] = "Aging (AR/AP)";
        ViewData["Module"] = "Finance";
        ViewData["ModuleSubtitle"] = "Customer and supplier balances with buckets.";
        return View();
    }

    private async Task<NewJournalPageViewModel> BuildNewJournalPageAsync(NewJournalEntryFormModel form, CancellationToken cancellationToken)
    {
        PadJournalLines(form);
        var accounts = await _db.GeneralLedgerAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.AccountCode)
            .Select(a => new GlAccountOptionViewModel { Id = a.Id, Code = a.AccountCode, Name = a.Name })
            .ToListAsync(cancellationToken);

        return new NewJournalPageViewModel { Form = form, Accounts = accounts };
    }

    private static void PadJournalLines(NewJournalEntryFormModel model)
    {
        model.Lines ??= new List<JournalLineFormRow>();
        while (model.Lines.Count < NewJournalEntryFormModel.LineSlotCount)
            model.Lines.Add(new JournalLineFormRow());
    }

    private static List<ParsedJournalLine> ParseJournalLines(NewJournalEntryFormModel model)
    {
        var result = new List<ParsedJournalLine>();
        foreach (var row in model.Lines)
        {
            if (row.AccountId is null || row.AccountId == Guid.Empty)
                continue;

            var hasDebit = TryParseMoney(row.Debit, out var debit);
            var hasCredit = TryParseMoney(row.Credit, out var credit);

            if (hasDebit && hasCredit)
                continue;

            if (!hasDebit && !hasCredit)
                continue;

            if (hasDebit)
                credit = 0m;
            else
                debit = 0m;

            if (debit < 0m || credit < 0m || debit > MaxJournalLineAmount || credit > MaxJournalLineAmount)
                continue;

            var lineMemo = string.IsNullOrWhiteSpace(row.LineMemo) ? null : row.LineMemo.Trim();
            result.Add(new ParsedJournalLine(row.AccountId.Value, debit, credit, lineMemo));
        }

        return result;
    }

    private static bool TryParseMoney(string? s, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        return decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    private async Task<string> AllocateJournalReferenceAsync(CancellationToken cancellationToken)
    {
        var prefix = $"JN-{DateTime.UtcNow:yyyyMMdd}-";

        var last = await _db.JournalEntries.AsNoTracking()
            .Where(j => j.Reference.StartsWith(prefix))
            .OrderByDescending(j => j.Reference)
            .Select(j => j.Reference)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(last) && last.Length >= prefix.Length + 4)
        {
            var suffix = last.AsSpan(prefix.Length);
            if (suffix.Length == 4 && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                next = n + 1;
        }

        if (next > 9999)
            throw new InvalidOperationException("Daily journal reference sequence exhausted.");

        return $"{prefix}{next:D4}";
    }

    private static decimal PostedBalance(GlAccountType type, decimal debits, decimal credits) =>
        type is GlAccountType.Asset or GlAccountType.Expense
            ? debits - credits
            : credits - debits;

    private async Task<AccountsPageViewModel> BuildAccountsPageAsync(CancellationToken cancellationToken,
        CreateGlAccountFormModel? createForm = null)
    {
        var aggregateList = await _db.JournalEntryLines.AsNoTracking()
            .GroupBy(l => l.GeneralLedgerAccountId)
            .Select(g => new { Id = g.Key, Debits = g.Sum(l => l.DebitAmount), Credits = g.Sum(l => l.CreditAmount) })
            .ToListAsync(cancellationToken);

        var aggregates = aggregateList.ToDictionary(x => x.Id, x => (x.Debits, x.Credits));

        var accounts = await _db.GeneralLedgerAccounts.AsNoTracking()
            .OrderBy(a => a.AccountCode)
            .ToListAsync(cancellationToken);

        var rows = accounts.Select(a =>
        {
            decimal debits = 0m;
            decimal credits = 0m;
            if (aggregates.TryGetValue(a.Id, out var pair))
            {
                debits = pair.Debits;
                credits = pair.Credits;
            }

            return new GlAccountRowViewModel
            {
                Id = a.Id,
                AccountCode = a.AccountCode,
                Name = a.Name,
                Type = a.Type,
                PostedBalance = PostedBalance(a.Type, debits, credits),
                IsActive = a.IsActive
            };
        }).ToList();

        return new AccountsPageViewModel
        {
            Accounts = rows,
            CreateForm = createForm ?? new CreateGlAccountFormModel()
        };
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
