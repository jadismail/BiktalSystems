using System.Globalization;
using Biktal.Domain.Finance;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Cashbox)]
public sealed class AccountsController : Controller
{
    // Match Accounts / POS UI (USD). Do not use the server OS culture (e.g. PLN).
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly ApplicationDbContext _db;
    private readonly CashboxSessionService _cashbox;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountsController(
        ApplicationDbContext db,
        CashboxSessionService cashbox,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _cashbox = cashbox;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Accounts";
        return View(await BuildPageAsync(date, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenSession(
        [Bind(Prefix = nameof(CashboxPageViewModel.OpenForm))] OpenCashboxSessionFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Accounts";
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (!ModelState.IsValid)
            return View("Index", await BuildPageAsync(today, cancellationToken, model, null));

        var actor = await ResolveActorDisplayAsync();
        var notes = CashboxSessionActorNotes.Stamp("Opened", actor, model.Notes);

        var openingCash = model.OpeningCashFloat ?? 0m;
        var openingWhish = model.OpeningWhishBalance ?? 0m;

        var result = await _cashbox.OpenSessionAsync(
            openingCash,
            openingWhish,
            notes,
            cancellationToken);

        if (!result.Success)
        {
            TempData["AccountsError"] = result.Error;
            return View("Index", await BuildPageAsync(today, cancellationToken, model, null));
        }

        TempData["AccountsMessage"] =
            $"Session opened by {actor} with {openingCash.ToString("C", MoneyCulture)} cash and {openingWhish.ToString("C", MoneyCulture)} Whish.";
        return RedirectToAction(nameof(Index), new { date = today.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSession(
        [Bind(Prefix = nameof(CashboxPageViewModel.CloseForm))] CloseCashboxSessionFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Accounts";
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (!ModelState.IsValid)
            return View("Index", await BuildPageAsync(today, cancellationToken, null, model));

        var actor = await ResolveActorDisplayAsync();
        var notes = CashboxSessionActorNotes.Stamp("Closed", actor, model.Notes);

        var result = await _cashbox.CloseSessionAsync(
            model.SessionId,
            model.CountedCash ?? 0m,
            model.CountedWhish ?? 0m,
            notes,
            cancellationToken);

        if (!result.Success)
        {
            TempData["AccountsError"] = result.Error;
            return View("Index", await BuildPageAsync(today, cancellationToken, null, model));
        }

        var cashMsg = FormatVarianceMessage("Cash", result.CashVariance);
        var whishMsg = FormatVarianceMessage("Whish", result.WhishVariance);
        TempData["AccountsMessage"] =
            $"Day closed by {actor}. Expected cash {result.ExpectedCash.ToString("C", MoneyCulture)}, Whish {result.ExpectedWhish.ToString("C", MoneyCulture)}. {cashMsg} {whishMsg}";
        return RedirectToAction(nameof(Index), new { date = today.ToString("yyyy-MM-dd") });
    }

    private async Task<CashboxPageViewModel> BuildPageAsync(
        DateOnly? date,
        CancellationToken cancellationToken,
        OpenCashboxSessionFormModel? openForm = null,
        CloseCashboxSessionFormModel? closeForm = null)
    {
        await FinanceDataSeeder.EnsureCashboxAccountDisplayNamesAsync(_db, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var selectedDate = date ?? today;
        var isToday = selectedDate == today;
        var periodLabel = selectedDate.ToString("dddd, MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

        var openSession = await _cashbox.GetOpenSessionAsync(cancellationToken);
        CashboxActiveSessionViewModel? active = null;
        CashboxSessionTotals? totals = null;

        // Only surface an open session when browsing that session's day (usually today).
        if (openSession is not null && openSession.SessionDate == selectedDate)
        {
            totals = await _cashbox.GetSessionPaymentTotalsAsync(openSession.Id, cancellationToken);
            active = new CashboxActiveSessionViewModel
            {
                Id = openSession.Id,
                SessionDate = openSession.SessionDate,
                OpenedAtUtc = openSession.OpenedAtUtc,
                OpenedBy = CashboxSessionActorNotes.ExtractActor(openSession.OpenNotes) ?? "—",
                OpeningCashFloat = openSession.OpeningCashFloat,
                OpeningWhishBalance = openSession.OpeningWhishBalance,
                CashSalesTotal = totals.CashTotal,
                WhishSalesTotal = totals.WhishTotal,
                ExpectedCash = CashboxSessionService.RoundMoney(openSession.OpeningCashFloat + totals.CashTotal),
                ExpectedWhish = CashboxSessionService.RoundMoney(openSession.OpeningWhishBalance + totals.WhishTotal),
                SaleCount = totals.SaleCount,
                OpenNotes = CashboxSessionActorNotes.ExtractUserNotes(openSession.OpenNotes)
            };
        }

        var dayClosed = await _db.CashboxSessions.AsNoTracking()
            .Where(s => s.Status == CashboxSessionStatus.Closed && s.SessionDate == selectedDate)
            .OrderByDescending(s => s.ClosedAtUtc)
            .ToListAsync(cancellationToken);

        var latestClosed = dayClosed.FirstOrDefault();

        decimal overviewCash;
        decimal overviewWhish;
        decimal overviewCashSales;
        decimal overviewWhishSales;
        string cashLabel;
        string whishLabel;
        string cashHint;
        string whishHint;

        if (active is not null)
        {
            overviewCash = active.ExpectedCash;
            overviewWhish = active.ExpectedWhish;
            overviewCashSales = active.CashSalesTotal;
            overviewWhishSales = active.WhishSalesTotal;
            cashLabel = "Expected Cash Amount";
            whishLabel = "Expected Whish Amount";
            cashHint = "Open session (until close)";
            whishHint = cashHint;
        }
        else if (latestClosed is not null)
        {
            overviewCash = latestClosed.CountedCash;
            overviewWhish = latestClosed.CountedWhish;
            overviewCashSales = latestClosed.CashSalesTotal;
            overviewWhishSales = latestClosed.WhishSalesTotal;
            cashLabel = "Closed Cash Amount";
            whishLabel = "Closed Whish Amount";
            cashHint = latestClosed.ClosedAtUtc is { } closedAt
                ? $"Closed at {closedAt.ToLocalTime():t}"
                : "Closed for this day";
            whishHint = cashHint;
        }
        else
        {
            overviewCash = 0m;
            overviewWhish = 0m;
            overviewCashSales = 0m;
            overviewWhishSales = 0m;
            cashLabel = "Closed Cash Amount";
            whishLabel = "Closed Whish Amount";
            cashHint = "No session closed on this day";
            whishHint = cashHint;
        }

        var accountRows = new List<CashboxPaymentAccountViewModel>
        {
            new()
            {
                AccountCode = FinanceDataSeeder.CashOnHandCode,
                Name = cashLabel,
                PostedBalance = overviewCash,
                SessionSales = overviewCashSales,
                Hint = cashHint
            },
            new()
            {
                AccountCode = FinanceDataSeeder.WhishWalletCode,
                Name = whishLabel,
                PostedBalance = overviewWhish,
                SessionSales = overviewWhishSales,
                Hint = whishHint
            }
        };

        var sessionIds = dayClosed.Select(s => s.Id).ToList();
        var repairCounts = sessionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.RepairTickets.AsNoTracking()
                .Where(t => t.CashboxSessionId != null && sessionIds.Contains(t.CashboxSessionId.Value))
                .GroupBy(t => t.CashboxSessionId!.Value)
                .Select(g => new { SessionId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SessionId, x => x.Count, cancellationToken);

        var dayCloses = dayClosed.Select(s => new CashboxClosedSessionRowViewModel
        {
            Id = s.Id,
            SessionDate = s.SessionDate,
            OpenedAtUtc = s.OpenedAtUtc,
            ClosedAtUtc = s.ClosedAtUtc!.Value,
            OpenedBy = CashboxSessionActorNotes.ExtractActor(s.OpenNotes) ?? "—",
            ClosedBy = CashboxSessionActorNotes.ExtractActor(s.CloseNotes) ?? "—",
            OpeningCashFloat = s.OpeningCashFloat,
            OpeningWhishBalance = s.OpeningWhishBalance,
            ExpectedCash = s.ExpectedCash,
            ExpectedWhish = s.ExpectedWhish,
            CountedCash = s.CountedCash,
            CountedWhish = s.CountedWhish,
            CashVariance = s.CashVariance,
            WhishVariance = s.WhishVariance,
            SaleCount = s.SaleCount,
            RepairCount = repairCounts.GetValueOrDefault(s.Id)
        }).ToList();

        // Same as before the day picker: on today, show Open when nothing is currently open.
        var canOpenSession = isToday && active is null && openSession is null;

        var close = closeForm ?? new CloseCashboxSessionFormModel();
        if (active is not null)
            close.SessionId = active.Id;

        return new CashboxPageViewModel
        {
            SelectedDate = selectedDate,
            PeriodLabel = periodLabel,
            IsToday = isToday,
            CanOpenSession = canOpenSession,
            ActiveSession = active,
            PaymentAccounts = accountRows,
            DayCloses = dayCloses,
            OpenForm = openForm ?? new OpenCashboxSessionFormModel(),
            CloseForm = close
        };
    }

    private async Task<string> ResolveActorDisplayAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is not null)
        {
            var display = string.IsNullOrWhiteSpace(user.DisplayName)
                ? null
                : user.DisplayName.Trim();
            var email = user.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(display) && !string.IsNullOrWhiteSpace(email))
                return $"{display} ({email})";
            if (!string.IsNullOrWhiteSpace(display))
                return display;
            if (!string.IsNullOrWhiteSpace(email))
                return email;
            if (!string.IsNullOrWhiteSpace(user.UserName))
                return user.UserName.Trim();
        }

        return User.Identity?.Name?.Trim() is { Length: > 0 } name
            ? name
            : "Unknown user";
    }

    private static string FormatVarianceMessage(string label, decimal variance)
    {
        if (variance == 0m)
            return $"{label} balanced.";

        return variance > 0m
            ? $"{label} over by {variance.ToString("C", MoneyCulture)}."
            : $"{label} short by {Math.Abs(variance).ToString("C", MoneyCulture)}.";
    }
}
