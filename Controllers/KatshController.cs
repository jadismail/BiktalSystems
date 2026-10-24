using System.Globalization;
using Biktal.Application.Security;
using Biktal.Domain.Finance;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Biktal.WebMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Sales)]
public sealed class KatshController : Controller
{
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");

    private static readonly (string Label, string Image, string AmountKey, decimal FaceUsd)[] AlfaVouchers =
    [
        ("Alfa 3.03", "alfa-3.03.jpg", "3.03", 3.03m),
        ("Alfa 4.5", "alfa-4.5.jpg", "4.5", 4.5m),
        ("Alfa 7.58", "alfa-7.58.jpg", "7.58", 7.58m),
        ("Alfa 10.0", "alfa-10.00.jpg", "10.0", 10.0m),
        ("Alfa 15.15", "alfa-15.15.jpg", "15.15", 15.15m),
        ("Alfa 22.73", "alfa-22.73.jpg", "22.73", 22.73m),
        ("Alfa 77.28", "alfa-77.28.jpg", "77.28", 77.28m)
    ];

    private static readonly (string Label, string Image, string AmountKey, decimal FaceUsd)[] TouchVouchers =
    [
        ("Touch SOS 1.22", "touch-sos-1.22.png", "sos-1.22", 1.22m),
        ("Touch Magic 1.67", "touch-magic-1.67.png", "magic-1.67", 1.67m),
        ("Touch Magic 3.79", "touch-magic-3.79.png", "magic-3.79", 3.79m),
        ("Touch Start 4.50", "touch-start-4.50.png", "start-4.50", 4.50m),
        ("Touch Magic 4.50", "touch-magic-4.50.png", "magic-4.50", 4.50m),
        ("Touch Smart 7.50", "touch-smart-7.50.png", "smart-7.50", 7.50m),
        ("Touch Magic 7.58", "touch-magic-7.58.png", "magic-7.58", 7.58m),
        ("Touch Magic 10.0", "touch-magic-10.00.png", "magic-10.0", 10.0m),
        ("Touch Super 13.50", "touch-super-13.50.png", "super-13.50", 13.50m),
        ("Touch Magic 15.15", "touch-magic-15.15.png", "magic-15.15", 15.15m),
        ("Touch Magic 22.73", "touch-magic-22.73.png", "magic-22.73", 22.73m),
        ("Touch Magic 77.28", "touch-magic-77.28.png", "magic-77.28", 77.28m)
    ];

    private readonly KatshVoucherPriceStore _prices;
    private readonly KatshAirtimePriceStore _airtimePrices;
    private readonly KatshMobileCreditsPriceStore _mobileCreditsPrices;
    private readonly KatshMobileCreditsBalanceStore _mobileCreditsBalances;
    private readonly KatshValidityDaysPriceStore _validityDaysPrices;
    private readonly KatshBalanceSessionStore _balances;
    private readonly KatshSaleHistoryStore _sales;
    private readonly CashboxSessionService _cashbox;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public KatshController(
        KatshVoucherPriceStore prices,
        KatshAirtimePriceStore airtimePrices,
        KatshMobileCreditsPriceStore mobileCreditsPrices,
        KatshMobileCreditsBalanceStore mobileCreditsBalances,
        KatshValidityDaysPriceStore validityDaysPrices,
        KatshBalanceSessionStore balances,
        KatshSaleHistoryStore sales,
        CashboxSessionService cashbox,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        _prices = prices;
        _airtimePrices = airtimePrices;
        _mobileCreditsPrices = mobileCreditsPrices;
        _mobileCreditsBalances = mobileCreditsBalances;
        _validityDaysPrices = validityDaysPrices;
        _balances = balances;
        _sales = sales;
        _cashbox = cashbox;
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Prepaid Vouchers";
        ViewData["Module"] = "Katsh";
        return View(await BuildIndexPageAsync(cancellationToken));
    }

    public async Task<IActionResult> Balance(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Balance";
        ViewData["Module"] = "Katsh";
        return View(await BuildBalancePageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenBalance(
        [Bind(Prefix = nameof(KatshBalancePageViewModel.OpenForm))] OpenKatshBalanceFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Balance";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildBalancePageAsync(cancellationToken);
            page.OpenForm = model;
            return View("Balance", page);
        }

        var actor = await ResolveActorDisplayAsync();
        var openUsd = KatshBalanceSessionStore.RoundUsd(model.BalanceUsd ?? 0m);
        var openLbp = KatshBalanceSessionStore.RoundLbp(model.BalanceLbp ?? 0m);
        var result = await _balances.OpenSessionAsync(openUsd, openLbp, actor, cancellationToken);

        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not open Katsh balances.";
            var page = await BuildBalancePageAsync(cancellationToken);
            page.OpenForm = model;
            return View("Balance", page);
        }

        await AppendBalanceHistoryAsync(
            "Opened balance",
            "balance:open",
            openUsd,
            openLbp,
            actor,
            cancellationToken);

        var usd = result.Session.BalanceUsd.ToString("C", MoneyCulture);
        var lbp = result.Session.BalanceLbp.ToString("N0", CultureInfo.InvariantCulture);
        TempData["KatshMessage"] = $"Katsh balances opened: {usd} · {lbp} L.L";
        return RedirectToAction(nameof(Balance));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> AdjustBalance(
        [Bind(Prefix = nameof(KatshBalancePageViewModel.AdjustForm))] OpenKatshBalanceFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Balance";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildBalancePageAsync(cancellationToken);
            page.AdjustForm = model;
            return View("Balance", page);
        }

        var before = await _balances.GetOpenSessionAsync(cancellationToken);
        if (before is null)
        {
            TempData["KatshError"] = "Open Katsh balances before updating them.";
            return RedirectToAction(nameof(Balance));
        }

        var newUsd = KatshBalanceSessionStore.RoundUsd(model.BalanceUsd ?? 0m);
        var newLbp = KatshBalanceSessionStore.RoundLbp(model.BalanceLbp ?? 0m);
        var result = await _balances.SetBalancesAsync(newUsd, newLbp, cancellationToken);

        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not update Katsh balances.";
            var page = await BuildBalancePageAsync(cancellationToken);
            page.AdjustForm = model;
            return View("Balance", page);
        }

        var actor = await ResolveActorDisplayAsync();
        var deltaUsd = KatshBalanceSessionStore.RoundUsd(newUsd - before.BalanceUsd);
        var deltaLbp = KatshBalanceSessionStore.RoundLbp(newLbp - before.BalanceLbp);
        await AppendBalanceHistoryAsync(
            "Balance set",
            "balance:set",
            deltaUsd,
            deltaLbp,
            actor,
            cancellationToken,
            allowZero: false,
            allowNegative: true);

        var usd = result.Session.BalanceUsd.ToString("C", MoneyCulture);
        var lbp = result.Session.BalanceLbp.ToString("N0", CultureInfo.InvariantCulture);
        TempData["KatshMessage"] = $"Katsh balances updated: {usd} · {lbp} L.L";
        return RedirectToAction(nameof(Balance));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBalance(
        [Bind(Prefix = nameof(KatshBalancePageViewModel.AddForm))] AddKatshBalanceFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Balance";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildBalancePageAsync(cancellationToken);
            page.AddForm = model;
            return View("Balance", page);
        }

        var addUsd = KatshBalanceSessionStore.RoundUsd(model.AmountUsd ?? 0m);
        var addLbp = KatshBalanceSessionStore.RoundLbp(model.AmountLbp ?? 0m);
        var result = await _balances.AddBalancesAsync(addUsd, addLbp, cancellationToken);

        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not add to Katsh balances.";
            var page = await BuildBalancePageAsync(cancellationToken);
            page.AddForm = model;
            return View("Balance", page);
        }

        var actor = await ResolveActorDisplayAsync();
        await AppendBalanceHistoryAsync(
            "Added to balance",
            "balance:add",
            addUsd,
            addLbp,
            actor,
            cancellationToken);

        var parts = new List<string>();
        if (addUsd > 0m)
            parts.Add(addUsd.ToString("C", MoneyCulture));
        if (addLbp > 0m)
            parts.Add($"{addLbp.ToString("N0", CultureInfo.InvariantCulture)} L.L");
        TempData["KatshMessage"] = $"Added to Katsh balances: {string.Join(" · ", parts)}";
        return RedirectToAction(nameof(Balance));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBalanceCarryOver(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Balance";
        ViewData["Module"] = "Katsh";

        var pending = await _balances.GetSnapshotAsync(cancellationToken);
        if (pending.State == KatshBalanceState.OpenToday)
            return RedirectToAction(nameof(Index));

        if (pending.State != KatshBalanceState.NeedsDayConfirmation)
        {
            TempData["KatshError"] = "Open an initial Katsh balance first.";
            return RedirectToAction(nameof(Balance));
        }

        var actor = await ResolveActorDisplayAsync();
        var result = await _balances.ConfirmDayAsync(actor, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not confirm today’s balance.";
            return RedirectToAction(nameof(Balance));
        }

        await AppendBalanceHistoryAsync(
            "Carried over balance",
            "balance:carry",
            result.Session.BalanceUsd,
            result.Session.BalanceLbp,
            actor,
            cancellationToken,
            allowZero: true);

        var usd = result.Session.BalanceUsd.ToString("C", MoneyCulture);
        var lbp = result.Session.BalanceLbp.ToString("N0", CultureInfo.InvariantCulture);
        TempData["KatshMessage"] = $"Today’s balance confirmed: {usd} · {lbp} L.L";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> History(
        string? mode,
        DateOnly? date,
        string? period,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "History";
        ViewData["Module"] = "Katsh";
        return View(await BuildHistoryPageAsync(mode, date, period, cancellationToken));
    }

    public async Task<IActionResult> Touch(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Touch";
        ViewData["Module"] = "Katsh";
        return View(await BuildTouchPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> SaveTouchPrices(
        [Bind(Prefix = nameof(KatshAlfaPageViewModel.EditForm))] KatshAlfaPricesFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Touch";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildTouchPageAsync(cancellationToken);
            page.EditForm = model;
            return View("Touch", page);
        }

        await _prices.SaveTouchPricesAsync(
            model.Rows.Select(r => new KatshVoucherPrice
            {
                AmountKey = r.AmountKey,
                PriceUsd = r.PriceUsd,
                PriceLbp = r.PriceLbp,
                CostUsd = r.CostUsd,
                CostLbp = r.CostLbp
            }),
            cancellationToken);

        TempData["KatshMessage"] = "Touch voucher prices saved.";
        return RedirectToAction(nameof(Touch));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SellTouchVoucher(
        SellKatshVoucherFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Touch";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectAfterVoucherSale(model.ReturnUrl, nameof(Touch));

        if (!ModelState.IsValid)
        {
            TempData["KatshError"] = "Could not confirm this voucher sale.";
            return RedirectToAction(nameof(Touch));
        }

        var voucher = TouchVouchers.FirstOrDefault(v =>
            string.Equals(v.AmountKey, model.AmountKey, StringComparison.Ordinal));
        if (voucher == default)
        {
            TempData["KatshError"] = "Unknown Touch voucher.";
            return RedirectToAction(nameof(Touch));
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            TempData["KatshError"] = "Choose a USD or LBP price.";
            return RedirectToAction(nameof(Touch));
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            TempData["KatshError"] = "Choose Cash or Whish.";
            return RedirectToAction(nameof(Touch));
        }

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _prices.GetTouchPricesAsync(lbpPerUsd, cancellationToken);
        priceMap.TryGetValue(voucher.AmountKey, out var price);

        var saleAmount = isUsd
            ? price?.PriceUsd ?? voucher.FaceUsd
            : price?.PriceLbp ?? 0m;
        var costAmount = isUsd
            ? price?.CostUsd ?? voucher.FaceUsd
            : price?.CostLbp ?? 0m;

        if (saleAmount <= 0m)
        {
            TempData["KatshError"] = "This voucher price is not set.";
            return RedirectToAction(nameof(Touch));
        }

        if (costAmount <= 0m)
        {
            TempData["KatshError"] = "This voucher cost is not set.";
            return RedirectToAction(nameof(Touch));
        }

        var currency = isUsd ? "USD" : "LBP";
        var result = await _balances.DeductAsync(currency, costAmount, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not deduct from Katsh balance.";
            return RedirectToAction(nameof(Touch));
        }

        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Touch",
                AmountKey = voucher.AmountKey,
                Label = voucher.Label,
                Currency = currency,
                Amount = saleAmount,
                Cost = costAmount,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var soldFor = isUsd
            ? saleAmount.ToString("C", MoneyCulture)
            : $"{saleAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        var costFor = isUsd
            ? costAmount.ToString("C", MoneyCulture)
            : $"{costAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        TempData["KatshMessage"] = $"{voucher.Label} sold for {soldFor} via {paymentMethod} (cost {costFor} deducted).";
        return RedirectAfterVoucherSale(model.ReturnUrl, nameof(Touch));
    }

    public async Task<IActionResult> Alfa(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Alfa";
        ViewData["Module"] = "Katsh";
        return View(await BuildAlfaPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> SaveAlfaPrices(
        [Bind(Prefix = nameof(KatshAlfaPageViewModel.EditForm))] KatshAlfaPricesFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Alfa";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildAlfaPageAsync(cancellationToken);
            page.EditForm = model;
            return View("Alfa", page);
        }

        await _prices.SaveAlfaPricesAsync(
            model.Rows.Select(r => new KatshVoucherPrice
            {
                AmountKey = r.AmountKey,
                PriceUsd = r.PriceUsd,
                PriceLbp = r.PriceLbp,
                CostUsd = r.CostUsd,
                CostLbp = r.CostLbp
            }),
            cancellationToken);

        TempData["KatshMessage"] = "Alfa voucher prices saved.";
        return RedirectToAction(nameof(Alfa));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SellAlfaVoucher(
        SellKatshVoucherFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Alfa";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectAfterVoucherSale(model.ReturnUrl, nameof(Alfa));

        if (!ModelState.IsValid)
        {
            TempData["KatshError"] = "Could not confirm this voucher sale.";
            return RedirectToAction(nameof(Alfa));
        }

        var voucher = AlfaVouchers.FirstOrDefault(v =>
            string.Equals(v.AmountKey, model.AmountKey, StringComparison.Ordinal));
        if (voucher == default)
        {
            TempData["KatshError"] = "Unknown Alfa voucher.";
            return RedirectToAction(nameof(Alfa));
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            TempData["KatshError"] = "Choose a USD or LBP price.";
            return RedirectToAction(nameof(Alfa));
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            TempData["KatshError"] = "Choose Cash or Whish.";
            return RedirectToAction(nameof(Alfa));
        }

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _prices.GetAlfaPricesAsync(lbpPerUsd, cancellationToken);
        priceMap.TryGetValue(voucher.AmountKey, out var price);

        var saleAmount = isUsd
            ? price?.PriceUsd ?? voucher.FaceUsd
            : price?.PriceLbp ?? 0m;
        var costAmount = isUsd
            ? price?.CostUsd ?? voucher.FaceUsd
            : price?.CostLbp ?? 0m;

        if (saleAmount <= 0m)
        {
            TempData["KatshError"] = "This voucher price is not set.";
            return RedirectToAction(nameof(Alfa));
        }

        if (costAmount <= 0m)
        {
            TempData["KatshError"] = "This voucher cost is not set.";
            return RedirectToAction(nameof(Alfa));
        }

        var currency = isUsd ? "USD" : "LBP";
        var result = await _balances.DeductAsync(currency, costAmount, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not deduct from Katsh balance.";
            return RedirectToAction(nameof(Alfa));
        }

        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Alfa",
                AmountKey = voucher.AmountKey,
                Label = voucher.Label,
                Currency = currency,
                Amount = saleAmount,
                Cost = costAmount,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var soldFor = isUsd
            ? saleAmount.ToString("C", MoneyCulture)
            : $"{saleAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        var costFor = isUsd
            ? costAmount.ToString("C", MoneyCulture)
            : $"{costAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        TempData["KatshMessage"] = $"{voucher.Label} sold for {soldFor} via {paymentMethod} (cost {costFor} deducted).";
        return RedirectAfterVoucherSale(model.ReturnUrl, nameof(Alfa));
    }

    public async Task<IActionResult> ICloud(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "iCloud";
        ViewData["Module"] = "Katsh";
        return View();
    }

    public async Task<IActionResult> PubG(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "PubG";
        ViewData["Module"] = "Katsh";
        return View();
    }

    public async Task<IActionResult> Airtime(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Airtime";
        ViewData["Module"] = "Katsh";
        return View(await BuildAirtimePageAsync(cancellationToken));
    }

    public async Task<IActionResult> CreditsOnMobile(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Credits on Mobile";
        ViewData["Module"] = "Katsh";
        return View(await BuildMobileCreditsPageAsync(cancellationToken));
    }

    public async Task<IActionResult> ValidityDays(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Validity Days";
        ViewData["Module"] = "Katsh";
        return View(await BuildValidityDaysPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> SaveValidityDaysPrices(
        [Bind(Prefix = nameof(KatshValidityDaysPageViewModel.EditForm))] KatshValidityDaysPricesFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Validity Days";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildValidityDaysPageAsync(cancellationToken);
            page.EditForm = model;
            return View("ValidityDays", page);
        }

        await _validityDaysPrices.SavePricesAsync(
            model.Rows.Select(r => new KatshValidityDaysPrice
            {
                Provider = r.Provider,
                Days = r.Days,
                PriceUsd = r.PriceUsd,
                PriceLbp = r.PriceLbp,
                CostUsd = r.CostUsd,
                CostLbp = r.CostLbp
            }),
            cancellationToken);

        TempData["KatshMessage"] = "Validity Days price and cost settings saved.";
        return RedirectToAction(nameof(ValidityDays));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SellValidityDays(
        [Bind(Prefix = nameof(KatshValidityDaysPageViewModel.SellForm))] SellKatshValidityDaysFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Validity Days";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectToAction(nameof(ValidityDays));

        if (!ModelState.IsValid)
        {
            var invalidPage = await BuildValidityDaysPageAsync(cancellationToken);
            invalidPage.SellForm = model;
            return View("ValidityDays", invalidPage);
        }

        var provider = KatshValidityDaysPriceStore.NormalizeProvider(model.Provider);
        if (provider is null)
        {
            ModelState.AddModelError(
                $"{nameof(KatshValidityDaysPageViewModel.SellForm)}.{nameof(model.Provider)}",
                "Choose Touch or Alfa.");
            var unknownPage = await BuildValidityDaysPageAsync(cancellationToken);
            unknownPage.SellForm = model;
            return View("ValidityDays", unknownPage);
        }

        var days = model.Days ?? 0;
        if (!KatshValidityDaysPriceStore.Durations.Contains(days))
        {
            ModelState.AddModelError(
                $"{nameof(KatshValidityDaysPageViewModel.SellForm)}.{nameof(model.Days)}",
                "Choose a validity period.");
            var daysPage = await BuildValidityDaysPageAsync(cancellationToken);
            daysPage.SellForm = model;
            return View("ValidityDays", daysPage);
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            ModelState.AddModelError(
                $"{nameof(KatshValidityDaysPageViewModel.SellForm)}.{nameof(model.Currency)}",
                "Choose USD or LBP.");
            var currencyPage = await BuildValidityDaysPageAsync(cancellationToken);
            currencyPage.SellForm = model;
            return View("ValidityDays", currencyPage);
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            ModelState.AddModelError(
                $"{nameof(KatshValidityDaysPageViewModel.SellForm)}.{nameof(model.PaymentMethod)}",
                "Choose Cash or Whish.");
            var payPage = await BuildValidityDaysPageAsync(cancellationToken);
            payPage.SellForm = model;
            return View("ValidityDays", payPage);
        }

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _validityDaysPrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        var amountKey = KatshValidityDaysPrice.MakeKey(provider, days);
        if (!priceMap.TryGetValue(amountKey, out var prices))
        {
            TempData["KatshError"] = "Validity Days prices are not set.";
            return RedirectToAction(nameof(ValidityDays));
        }

        var saleAmount = isUsd ? prices.PriceUsd : prices.PriceLbp;
        var costAmount = isUsd ? prices.CostUsd : prices.CostLbp;
        if (saleAmount <= 0m)
        {
            TempData["KatshError"] = "This selling price is not set.";
            return RedirectToAction(nameof(ValidityDays));
        }

        if (costAmount <= 0m)
        {
            TempData["KatshError"] = "This cost is not set.";
            return RedirectToAction(nameof(ValidityDays));
        }

        var currency = isUsd ? "USD" : "LBP";
        saleAmount = isUsd
            ? KatshBalanceSessionStore.RoundUsd(saleAmount)
            : KatshBalanceSessionStore.RoundLbp(saleAmount);
        costAmount = isUsd
            ? KatshBalanceSessionStore.RoundUsd(costAmount)
            : KatshBalanceSessionStore.RoundLbp(costAmount);

        var result = await _balances.DeductAsync(currency, costAmount, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not deduct from Katsh balance.";
            return RedirectToAction(nameof(ValidityDays));
        }

        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        var label = $"{provider} · {KatshValidityDaysPriceStore.LabelFor(days)}";
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Validity Days",
                AmountKey = amountKey,
                Label = label,
                Currency = currency,
                Amount = saleAmount,
                Cost = costAmount,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var soldFor = isUsd
            ? saleAmount.ToString("C", MoneyCulture)
            : $"{saleAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        var costFor = isUsd
            ? costAmount.ToString("C", MoneyCulture)
            : $"{costAmount.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        TempData["KatshMessage"] = $"{label} sold for {soldFor} via {paymentMethod} (cost {costFor} deducted).";
        return RedirectToAction(nameof(ValidityDays));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> SaveMobileCreditsPrices(
        [Bind(Prefix = nameof(KatshMobileCreditsPageViewModel.EditForm))] KatshMobileCreditsPricesFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Credits on Mobile";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildMobileCreditsPageAsync(cancellationToken);
            page.EditForm = model;
            return View("CreditsOnMobile", page);
        }

        await _mobileCreditsPrices.SavePricesAsync(
            model.Rows.Select(r => new KatshMobileCreditsProviderPrices
            {
                Provider = r.Provider,
                PriceUsd = r.PriceUsd,
                PriceLbp = r.PriceLbp,
                CostOneUsd = r.CostOneUsd,
                CostOneLbp = r.CostOneLbp,
                CostTwoUsd = r.CostTwoUsd,
                CostTwoLbp = r.CostTwoLbp,
                CostThreePlusUsd = r.CostThreePlusUsd,
                CostThreePlusLbp = r.CostThreePlusLbp
            }),
            cancellationToken);

        TempData["KatshMessage"] = "Credits on Mobile price and cost settings saved.";
        return RedirectToAction(nameof(CreditsOnMobile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMobileCreditsBalance(
        [Bind(Prefix = nameof(KatshMobileCreditsPageViewModel.AddForm))] AddKatshMobileCreditsFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Credits on Mobile";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildMobileCreditsPageAsync(cancellationToken);
            page.AddForm = model;
            return View("CreditsOnMobile", page);
        }

        var touch = model.TouchCredits ?? 0;
        var alfa = model.AlfaCredits ?? 0;
        var actor = await ResolveActorDisplayAsync();
        var result = await _mobileCreditsBalances.AddAsync(touch, alfa, actor, cancellationToken);
        if (!result.Success || result.Balances is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not add mobile credits.";
            return RedirectToAction(nameof(CreditsOnMobile));
        }

        var parts = new List<string>();
        if (touch > 0)
            parts.Add($"Touch +{touch}");
        if (alfa > 0)
            parts.Add($"Alfa +{alfa}");
        TempData["KatshMessage"] =
            $"Added mobile credits: {string.Join(" · ", parts)}. " +
            $"Now Touch {result.Balances.TouchCredits} · Alfa {result.Balances.AlfaCredits}.";
        return RedirectToAction(nameof(CreditsOnMobile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SellMobileCredits(
        [Bind(Prefix = nameof(KatshMobileCreditsPageViewModel.SellForm))] SellKatshMobileCreditsFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Credits on Mobile";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectToAction(nameof(CreditsOnMobile));

        if (!ModelState.IsValid)
        {
            var invalidPage = await BuildMobileCreditsPageAsync(cancellationToken);
            invalidPage.SellForm = model;
            return View("CreditsOnMobile", invalidPage);
        }

        var provider = KatshMobileCreditsBalanceStore.NormalizeProvider(model.Provider);
        if (provider is null)
        {
            ModelState.AddModelError(
                $"{nameof(KatshMobileCreditsPageViewModel.SellForm)}.{nameof(model.Provider)}",
                "Choose Touch or Alfa.");
            var unknownPage = await BuildMobileCreditsPageAsync(cancellationToken);
            unknownPage.SellForm = model;
            return View("CreditsOnMobile", unknownPage);
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            ModelState.AddModelError(
                $"{nameof(KatshMobileCreditsPageViewModel.SellForm)}.{nameof(model.Currency)}",
                "Choose USD or LBP.");
            var currencyPage = await BuildMobileCreditsPageAsync(cancellationToken);
            currencyPage.SellForm = model;
            return View("CreditsOnMobile", currencyPage);
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            ModelState.AddModelError(
                $"{nameof(KatshMobileCreditsPageViewModel.SellForm)}.{nameof(model.PaymentMethod)}",
                "Choose Cash or Whish.");
            var payPage = await BuildMobileCreditsPageAsync(cancellationToken);
            payPage.SellForm = model;
            return View("CreditsOnMobile", payPage);
        }

        var credits = model.Credits ?? 0;
        if (credits < 1)
        {
            ModelState.AddModelError(
                $"{nameof(KatshMobileCreditsPageViewModel.SellForm)}.{nameof(model.Credits)}",
                "Enter credits sold.");
            var creditsPage = await BuildMobileCreditsPageAsync(cancellationToken);
            creditsPage.SellForm = model;
            return View("CreditsOnMobile", creditsPage);
        }

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _mobileCreditsPrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        if (!priceMap.TryGetValue(provider, out var prices))
        {
            TempData["KatshError"] = "Credits on Mobile prices are not set.";
            return RedirectToAction(nameof(CreditsOnMobile));
        }

        var unitPrice = KatshMobileCreditsPriceStore.ResolveUnitPrice(prices, isUsd);
        var unitCost = KatshMobileCreditsPriceStore.ResolveUnitCost(prices, credits, isUsd);
        if (unitPrice <= 0m)
        {
            TempData["KatshError"] = "This selling price is not set.";
            return RedirectToAction(nameof(CreditsOnMobile));
        }

        if (unitCost <= 0m)
        {
            TempData["KatshError"] = "This cost tier is not set.";
            return RedirectToAction(nameof(CreditsOnMobile));
        }

        var deduct = await _mobileCreditsBalances.DeductAsync(provider, credits, cancellationToken);
        if (!deduct.Success || deduct.Balances is null)
        {
            TempData["KatshError"] = deduct.Error ?? "Could not deduct from mobile credit balance.";
            return RedirectToAction(nameof(CreditsOnMobile));
        }

        var currency = isUsd ? "USD" : "LBP";
        var saleTotal = isUsd
            ? KatshBalanceSessionStore.RoundUsd(unitPrice * credits)
            : KatshBalanceSessionStore.RoundLbp(unitPrice * credits);
        var costTotal = isUsd
            ? KatshBalanceSessionStore.RoundUsd(unitCost * credits)
            : KatshBalanceSessionStore.RoundLbp(unitCost * credits);

        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        var creditWord = credits == 1 ? "credit" : "credits";
        var label = $"{provider} · {credits} {creditWord}";
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Credits on Mobile",
                AmountKey = $"{provider.ToLowerInvariant()}:{credits}",
                Label = label,
                Currency = currency,
                Amount = saleTotal,
                Cost = costTotal,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var soldFor = isUsd
            ? saleTotal.ToString("C", MoneyCulture)
            : $"{saleTotal.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        var remaining = provider == "Alfa"
            ? deduct.Balances.AlfaCredits
            : deduct.Balances.TouchCredits;
        TempData["KatshMessage"] =
            $"{label} sold for {soldFor} via {paymentMethod}. " +
            $"{provider} balance now {remaining} credit{(remaining == 1 ? "" : "s")}.";
        return RedirectToAction(nameof(CreditsOnMobile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoleGroups.OwnerAdmin)]
    public async Task<IActionResult> SaveAirtimePrices(
        [Bind(Prefix = nameof(KatshAirtimePageViewModel.EditForm))] KatshAirtimePricesFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Airtime";
        ViewData["Module"] = "Katsh";

        if (!ModelState.IsValid)
        {
            var page = await BuildAirtimePageAsync(cancellationToken);
            page.EditForm = model;
            return View("Airtime", page);
        }

        await _airtimePrices.SavePricesAsync(
            model.Rows.Select(r => new KatshAirtimeProviderPrices
            {
                Provider = r.Provider,
                PriceUsd = r.PriceUsd,
                PriceLbp = r.PriceLbp,
                CostOneUsd = r.CostOneUsd,
                CostOneLbp = r.CostOneLbp,
                CostTwoUsd = r.CostTwoUsd,
                CostTwoLbp = r.CostTwoLbp,
                CostThreePlusUsd = r.CostThreePlusUsd,
                CostThreePlusLbp = r.CostThreePlusLbp
            }),
            cancellationToken);

        TempData["KatshMessage"] = "Airtime price and cost settings saved.";
        return RedirectToAction(nameof(Airtime));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SellAirtime(
        [Bind(Prefix = nameof(KatshAirtimePageViewModel.SellForm))] SellKatshAirtimeFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Airtime";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectToAction(nameof(Airtime));

        if (!ModelState.IsValid)
        {
            var invalidPage = await BuildAirtimePageAsync(cancellationToken);
            invalidPage.SellForm = model;
            return View("Airtime", invalidPage);
        }

        var provider = KatshAirtimePriceStore.Providers.FirstOrDefault(p =>
            string.Equals(p, model.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            ModelState.AddModelError($"{nameof(KatshAirtimePageViewModel.SellForm)}.{nameof(model.Provider)}", "Choose Touch or Alfa.");
            var unknownPage = await BuildAirtimePageAsync(cancellationToken);
            unknownPage.SellForm = model;
            return View("Airtime", unknownPage);
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            ModelState.AddModelError($"{nameof(KatshAirtimePageViewModel.SellForm)}.{nameof(model.Currency)}", "Choose USD or LBP.");
            var currencyPage = await BuildAirtimePageAsync(cancellationToken);
            currencyPage.SellForm = model;
            return View("Airtime", currencyPage);
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            ModelState.AddModelError($"{nameof(KatshAirtimePageViewModel.SellForm)}.{nameof(model.PaymentMethod)}", "Choose Cash or Whish.");
            var payPage = await BuildAirtimePageAsync(cancellationToken);
            payPage.SellForm = model;
            return View("Airtime", payPage);
        }

        var credits = model.Credits ?? 0;
        if (credits < 1)
        {
            ModelState.AddModelError($"{nameof(KatshAirtimePageViewModel.SellForm)}.{nameof(model.Credits)}", "Enter credits sold.");
            var creditsPage = await BuildAirtimePageAsync(cancellationToken);
            creditsPage.SellForm = model;
            return View("Airtime", creditsPage);
        }

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _airtimePrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        if (!priceMap.TryGetValue(provider, out var prices))
        {
            TempData["KatshError"] = "Airtime prices are not set.";
            return RedirectToAction(nameof(Airtime));
        }

        var unitPrice = KatshAirtimePriceStore.ResolveUnitPrice(prices, isUsd);
        var unitCost = KatshAirtimePriceStore.ResolveUnitCost(prices, credits, isUsd);
        if (unitPrice <= 0m)
        {
            TempData["KatshError"] = "This airtime selling price is not set.";
            return RedirectToAction(nameof(Airtime));
        }

        if (unitCost <= 0m)
        {
            TempData["KatshError"] = "This airtime cost tier is not set.";
            return RedirectToAction(nameof(Airtime));
        }

        var currency = isUsd ? "USD" : "LBP";
        var saleTotal = isUsd
            ? KatshBalanceSessionStore.RoundUsd(unitPrice * credits)
            : KatshBalanceSessionStore.RoundLbp(unitPrice * credits);
        var costTotal = isUsd
            ? KatshBalanceSessionStore.RoundUsd(unitCost * credits)
            : KatshBalanceSessionStore.RoundLbp(unitCost * credits);

        var result = await _balances.DeductAsync(currency, costTotal, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not deduct from Katsh balance.";
            return RedirectToAction(nameof(Airtime));
        }

        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        var creditWord = credits == 1 ? "credit" : "credits";
        var label = $"{provider} · {credits} {creditWord}";
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Airtime",
                AmountKey = $"{provider.ToLowerInvariant()}:{credits}",
                Label = label,
                Currency = currency,
                Amount = saleTotal,
                Cost = costTotal,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var soldFor = isUsd
            ? saleTotal.ToString("C", MoneyCulture)
            : $"{saleTotal.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        var costFor = isUsd
            ? costTotal.ToString("C", MoneyCulture)
            : $"{costTotal.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        TempData["KatshMessage"] = $"{label} sold for {soldFor} via {paymentMethod} (cost {costFor} deducted).";
        return RedirectToAction(nameof(Airtime));
    }

    public async Task<IActionResult> Other(CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Other";
        ViewData["Module"] = "Katsh";
        return View(await BuildOtherPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualSale(
        [Bind(Prefix = nameof(KatshOtherPageViewModel.Form))] CreateKatshManualSaleFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await EnsureBalanceOpenAsync(cancellationToken))
            return RedirectToAction(nameof(Balance));

        ViewData["Title"] = "Other";
        ViewData["Module"] = "Katsh";

        var accountsSession = await RequireAccountsSessionAsync(cancellationToken);
        if (accountsSession is null)
            return RedirectToAction(nameof(Other));

        if (!ModelState.IsValid)
        {
            var page = await BuildOtherPageAsync(cancellationToken);
            page.Form = model;
            return View("Other", page);
        }

        var isUsd = string.Equals(model.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(model.Currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
        {
            ModelState.AddModelError($"{nameof(KatshOtherPageViewModel.Form)}.{nameof(model.Currency)}", "Choose USD or LBP.");
            var page = await BuildOtherPageAsync(cancellationToken);
            page.Form = model;
            return View("Other", page);
        }

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        if (paymentMethod is null)
        {
            ModelState.AddModelError($"{nameof(KatshOtherPageViewModel.Form)}.{nameof(model.PaymentMethod)}", "Choose Cash or Whish.");
            var page = await BuildOtherPageAsync(cancellationToken);
            page.Form = model;
            return View("Other", page);
        }

        var amount = model.Amount ?? 0m;
        if (amount <= 0m)
        {
            ModelState.AddModelError($"{nameof(KatshOtherPageViewModel.Form)}.{nameof(model.Amount)}", "Amount must be greater than zero.");
            var page = await BuildOtherPageAsync(cancellationToken);
            page.Form = model;
            return View("Other", page);
        }

        var currency = isUsd ? "USD" : "LBP";
        var charge = isUsd
            ? KatshBalanceSessionStore.RoundUsd(amount)
            : KatshBalanceSessionStore.RoundLbp(amount);

        var result = await _balances.DeductAsync(currency, charge, cancellationToken);
        if (!result.Success || result.Session is null)
        {
            TempData["KatshError"] = result.Error ?? "Could not deduct from Katsh balance.";
            return RedirectToAction(nameof(Other));
        }

        var label = model.Label.Trim();
        var soldAtUtc = DateTime.UtcNow;
        var actor = await ResolveActorDisplayAsync();
        await _sales.AppendAsync(
            new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = soldAtUtc,
                SaleDate = DateOnly.FromDateTime(soldAtUtc.ToLocalTime()),
                Provider = "Other",
                AmountKey = "manual",
                Label = label,
                Currency = currency,
                Amount = charge,
                Cost = charge,
                PaymentMethod = paymentMethod,
                CashboxSessionId = accountsSession.Id,
                SoldBy = actor
            },
            cancellationToken);

        var charged = isUsd
            ? charge.ToString("C", MoneyCulture)
            : $"{charge.ToString("N0", CultureInfo.InvariantCulture)} L.L";
        TempData["KatshMessage"] = $"{label} sold for {charged} via {paymentMethod}.";
        return RedirectToAction(nameof(Other));
    }

    private async Task<bool> EnsureBalanceOpenAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _balances.GetSnapshotAsync(cancellationToken);
        if (snapshot.State == KatshBalanceState.OpenToday)
            return true;

        TempData["KatshError"] = snapshot.State == KatshBalanceState.NeedsDayConfirmation
            ? "Confirm today’s carried balance before using Katsh."
            : "Open USD and LBP balances before using Katsh.";
        return false;
    }

    private async Task<KatshIndexPageViewModel> BuildIndexPageAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _balances.GetSnapshotAsync(cancellationToken);
        var isOpen = snapshot.State == KatshBalanceState.OpenToday;
        var searchVouchers = isOpen
            ? await BuildSearchVouchersAsync(cancellationToken)
            : Array.Empty<KatshSearchVoucherCardViewModel>();

        return new KatshIndexPageViewModel
        {
            IsBalanceOpen = isOpen,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            NeedsDayConfirmation = snapshot.State == KatshBalanceState.NeedsDayConfirmation,
            BalanceUsd = isOpen ? snapshot.BalanceUsd : 0m,
            BalanceLbp = isOpen ? snapshot.BalanceLbp : 0m,
            OpenedBy = snapshot.OpenedBy,
            SearchVouchers = searchVouchers
        };
    }

    private async Task<IReadOnlyList<KatshSearchVoucherCardViewModel>> BuildSearchVouchersAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var alfaPrices = await _prices.GetAlfaPricesAsync(lbpPerUsd, cancellationToken);
        var touchPrices = await _prices.GetTouchPricesAsync(lbpPerUsd, cancellationToken);

        var list = new List<KatshSearchVoucherCardViewModel>();

        void AddRange(
            string provider,
            string imageFolder,
            (string Label, string Image, string AmountKey, decimal FaceUsd)[] catalog,
            IReadOnlyDictionary<string, KatshVoucherPrice> prices)
        {
            foreach (var v in catalog)
            {
                prices.TryGetValue(v.AmountKey, out var price);
                var faceAlt = v.FaceUsd.ToString("0.##", CultureInfo.InvariantCulture);
                var faceFull = v.FaceUsd.ToString("0.00", CultureInfo.InvariantCulture);
                list.Add(new KatshSearchVoucherCardViewModel
                {
                    Provider = provider,
                    Label = v.Label,
                    ImageFolder = imageFolder,
                    Image = v.Image,
                    AmountKey = v.AmountKey,
                    FaceUsd = v.FaceUsd,
                    PriceUsd = price?.PriceUsd ?? v.FaceUsd,
                    PriceLbp = price?.PriceLbp ?? 0m,
                    CostUsd = price?.CostUsd ?? v.FaceUsd,
                    CostLbp = price?.CostLbp ?? 0m,
                    SearchText = $"{v.Label} {v.AmountKey} {faceAlt} {faceFull} {provider}"
                });
            }
        }

        AddRange("Touch", "touch", TouchVouchers, touchPrices);
        AddRange("Alfa", "alfa", AlfaVouchers, alfaPrices);
        return list;
    }

    private static string? NormalizePaymentMethod(string? value)
    {
        if (string.Equals(value, "Cash", StringComparison.OrdinalIgnoreCase))
            return "Cash";
        if (string.Equals(value, "Whish", StringComparison.OrdinalIgnoreCase))
            return "Whish";
        return null;
    }

    private async Task<CashboxSession?> RequireAccountsSessionAsync(CancellationToken cancellationToken)
    {
        var session = await _cashbox.GetOpenSessionAsync(cancellationToken);
        if (session is not null)
            return session;

        TempData["KatshError"] = "Open today's Accounts session before selling Katsh.";
        return null;
    }

    private async Task<bool> IsAccountsSessionOpenAsync(CancellationToken cancellationToken) =>
        await _cashbox.GetOpenSessionAsync(cancellationToken) is not null;

    private IActionResult RedirectAfterVoucherSale(string? returnUrl, string fallbackAction)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(fallbackAction);
    }

    private async Task<KatshBalancePageViewModel> BuildBalancePageAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _balances.GetSnapshotAsync(cancellationToken);
        var isOpen = snapshot.State == KatshBalanceState.OpenToday;
        var needsConfirm = snapshot.State == KatshBalanceState.NeedsDayConfirmation;
        return new KatshBalancePageViewModel
        {
            IsBalanceOpen = isOpen,
            NeedsDayConfirmation = needsConfirm,
            NeedsInitialOpen = snapshot.State == KatshBalanceState.NotInitialized,
            CanManageBalance = User.IsInRole(ApplicationRoles.Admin),
            BalanceUsd = snapshot.BalanceUsd,
            BalanceLbp = snapshot.BalanceLbp,
            CarryFromDate = needsConfirm ? snapshot.SessionDate : null,
            OpenedBy = snapshot.OpenedBy,
            ConfirmedBy = snapshot.ConfirmedBy ?? snapshot.OpenedBy,
            OpenedAtLocal = isOpen
                ? (snapshot.ConfirmedAtUtc ?? snapshot.OpenedAtUtc)?.ToLocalTime()
                : snapshot.OpenedAtUtc?.ToLocalTime(),
            OpenForm = new OpenKatshBalanceFormModel(),
            AdjustForm = new OpenKatshBalanceFormModel(),
            AddForm = new AddKatshBalanceFormModel()
        };
    }

    private async Task AppendBalanceHistoryAsync(
        string label,
        string amountKey,
        decimal amountUsd,
        decimal amountLbp,
        string actor,
        CancellationToken cancellationToken,
        bool allowZero = false,
        bool allowNegative = false)
    {
        var atUtc = DateTime.UtcNow;
        var saleDate = DateOnly.FromDateTime(atUtc.ToLocalTime());
        var records = new List<KatshSaleRecord>();

        void TryAdd(string currency, decimal amount)
        {
            if (amount == 0m && !allowZero)
                return;
            if (amount < 0m && !allowNegative)
                return;

            records.Add(new KatshSaleRecord
            {
                Id = Guid.NewGuid(),
                SoldAtUtc = atUtc,
                SaleDate = saleDate,
                Provider = "Balance",
                AmountKey = amountKey,
                Label = label,
                Currency = currency,
                Amount = amount,
                SoldBy = actor
            });
        }

        TryAdd("USD", amountUsd);
        TryAdd("LBP", amountLbp);
        await _sales.AppendRangeAsync(records, cancellationToken);
    }

    private async Task<KatshHistoryPageViewModel> BuildHistoryPageAsync(
        string? mode,
        DateOnly? date,
        string? period,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isMonthly = string.Equals(mode, "monthly", StringComparison.OrdinalIgnoreCase);
        var selectedMode = isMonthly ? "monthly" : "daily";

        DateOnly selectedDate;
        int year;
        int month;
        string periodLabel;
        string dateInput;
        string monthInput;
        IReadOnlyList<KatshSaleRecord> sales;

        if (isMonthly)
        {
            if (!TryParseMonth(period, out year, out month))
            {
                year = today.Year;
                month = today.Month;
            }

            selectedDate = new DateOnly(year, month, 1);
            dateInput = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            monthInput = $"{year:D4}-{month:D2}";
            periodLabel = selectedDate.ToString("MMMM yyyy", MoneyCulture);
            sales = await _sales.GetByMonthAsync(year, month, cancellationToken);
        }
        else
        {
            selectedDate = date ?? today;
            year = selectedDate.Year;
            month = selectedDate.Month;
            dateInput = selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            monthInput = $"{year:D4}-{month:D2}";
            periodLabel = selectedDate.ToString("dddd, MMM d, yyyy", MoneyCulture);
            sales = await _sales.GetByDateAsync(selectedDate, cancellationToken);
        }

        var rows = sales.Select(s => new KatshHistorySaleRowViewModel
        {
            SoldAtLocal = s.SoldAtUtc.ToLocalTime(),
            SaleDate = s.SaleDate,
            Provider = s.Provider,
            Label = s.Label,
            Currency = s.Currency,
            Amount = s.Amount,
            PaymentMethod = s.PaymentMethod,
            SoldBy = s.SoldBy,
            IsBalanceEntry = string.Equals(s.Provider, "Balance", StringComparison.OrdinalIgnoreCase)
        }).ToList();

        var saleRows = rows.Where(r => !r.IsBalanceEntry).ToList();

        return new KatshHistoryPageViewModel
        {
            Mode = selectedMode,
            SelectedDate = selectedDate,
            SelectedYear = year,
            SelectedMonth = month,
            DateInputValue = dateInput,
            MonthInputValue = monthInput,
            PeriodLabel = periodLabel,
            SaleCount = saleRows.Count,
            TotalUsd = saleRows.Where(r => string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount),
            TotalLbp = saleRows.Where(r => string.Equals(r.Currency, "LBP", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount),
            Sales = rows
        };
    }

    private static bool TryParseMonth(string? period, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(period))
            return false;

        if (DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            year = d.Year;
            month = d.Month;
            return true;
        }

        var parts = period.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && year is >= 2000 and <= 2100
            && month is >= 1 and <= 12)
        {
            return true;
        }

        return false;
    }

    private async Task<KatshOtherPageViewModel> BuildOtherPageAsync(CancellationToken cancellationToken)
    {
        var balance = await _balances.GetOpenSessionAsync(cancellationToken);
        return new KatshOtherPageViewModel
        {
            BalanceUsd = balance?.BalanceUsd ?? 0m,
            BalanceLbp = balance?.BalanceLbp ?? 0m,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            Form = new CreateKatshManualSaleFormModel()
        };
    }

    private async Task<KatshValidityDaysPageViewModel> BuildValidityDaysPageAsync(
        CancellationToken cancellationToken,
        SellKatshValidityDaysFormModel? sellForm = null)
    {
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _validityDaysPrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        var balance = await _balances.GetOpenSessionAsync(cancellationToken);
        var canEdit = User.IsInRole(ApplicationRoles.Admin);

        var options = new List<KatshValidityDaysOptionViewModel>();
        foreach (var provider in KatshValidityDaysPriceStore.Providers)
        {
            foreach (var days in KatshValidityDaysPriceStore.Durations)
            {
                var key = KatshValidityDaysPrice.MakeKey(provider, days);
                priceMap.TryGetValue(key, out var prices);
                prices ??= new KatshValidityDaysPrice { Provider = provider, Days = days };
                options.Add(new KatshValidityDaysOptionViewModel
                {
                    Provider = provider,
                    Days = days,
                    Label = KatshValidityDaysPriceStore.LabelFor(days),
                    AmountKey = key,
                    PriceUsd = prices.PriceUsd,
                    PriceLbp = prices.PriceLbp,
                    CostUsd = prices.CostUsd,
                    CostLbp = prices.CostLbp
                });
            }
        }

        return new KatshValidityDaysPageViewModel
        {
            BalanceUsd = balance?.BalanceUsd ?? 0m,
            BalanceLbp = balance?.BalanceLbp ?? 0m,
            CanEditPrices = canEdit,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            Options = options,
            EditForm = new KatshValidityDaysPricesFormModel
            {
                Rows = options.Select(o => new KatshValidityDaysPriceRowFormModel
                {
                    Provider = o.Provider,
                    Days = o.Days,
                    PriceUsd = o.PriceUsd,
                    PriceLbp = o.PriceLbp,
                    CostUsd = o.CostUsd,
                    CostLbp = o.CostLbp
                }).ToList()
            },
            SellForm = sellForm ?? new SellKatshValidityDaysFormModel()
        };
    }

    private async Task<KatshMobileCreditsPageViewModel> BuildMobileCreditsPageAsync(
        CancellationToken cancellationToken,
        SellKatshMobileCreditsFormModel? sellForm = null)
    {
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _mobileCreditsPrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        var balances = await _mobileCreditsBalances.GetAsync(cancellationToken);
        var canEdit = User.IsInRole(ApplicationRoles.Admin);

        var providers = KatshMobileCreditsPriceStore.Providers.Select(provider =>
        {
            priceMap.TryGetValue(provider, out var prices);
            prices ??= new KatshMobileCreditsProviderPrices { Provider = provider };
            return new KatshMobileCreditsProviderPriceViewModel
            {
                Provider = provider,
                PriceUsd = prices.PriceUsd,
                PriceLbp = prices.PriceLbp,
                CostOneUsd = prices.CostOneUsd,
                CostOneLbp = prices.CostOneLbp,
                CostTwoUsd = prices.CostTwoUsd,
                CostTwoLbp = prices.CostTwoLbp,
                CostThreePlusUsd = prices.CostThreePlusUsd,
                CostThreePlusLbp = prices.CostThreePlusLbp
            };
        }).ToList();

        return new KatshMobileCreditsPageViewModel
        {
            TouchCredits = balances.TouchCredits,
            AlfaCredits = balances.AlfaCredits,
            CanEditPrices = canEdit,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            Providers = providers,
            EditForm = new KatshMobileCreditsPricesFormModel
            {
                Rows = providers.Select(p => new KatshMobileCreditsPriceRowFormModel
                {
                    Provider = p.Provider,
                    PriceUsd = p.PriceUsd,
                    PriceLbp = p.PriceLbp,
                    CostOneUsd = p.CostOneUsd,
                    CostOneLbp = p.CostOneLbp,
                    CostTwoUsd = p.CostTwoUsd,
                    CostTwoLbp = p.CostTwoLbp,
                    CostThreePlusUsd = p.CostThreePlusUsd,
                    CostThreePlusLbp = p.CostThreePlusLbp
                }).ToList()
            },
            SellForm = sellForm ?? new SellKatshMobileCreditsFormModel(),
            AddForm = new AddKatshMobileCreditsFormModel()
        };
    }

    private async Task<KatshAirtimePageViewModel> BuildAirtimePageAsync(
        CancellationToken cancellationToken,
        SellKatshAirtimeFormModel? sellForm = null)
    {
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await _airtimePrices.GetPricesAsync(lbpPerUsd, cancellationToken);
        var balance = await _balances.GetOpenSessionAsync(cancellationToken);
        var canEdit = User.IsInRole(ApplicationRoles.Admin);

        var providers = KatshAirtimePriceStore.Providers.Select(provider =>
        {
            priceMap.TryGetValue(provider, out var prices);
            prices ??= new KatshAirtimeProviderPrices { Provider = provider };
            return new KatshAirtimeProviderPriceViewModel
            {
                Provider = provider,
                PriceUsd = prices.PriceUsd,
                PriceLbp = prices.PriceLbp,
                CostOneUsd = prices.CostOneUsd,
                CostOneLbp = prices.CostOneLbp,
                CostTwoUsd = prices.CostTwoUsd,
                CostTwoLbp = prices.CostTwoLbp,
                CostThreePlusUsd = prices.CostThreePlusUsd,
                CostThreePlusLbp = prices.CostThreePlusLbp
            };
        }).ToList();

        return new KatshAirtimePageViewModel
        {
            BalanceUsd = balance?.BalanceUsd ?? 0m,
            BalanceLbp = balance?.BalanceLbp ?? 0m,
            CanEditPrices = canEdit,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            Providers = providers,
            EditForm = new KatshAirtimePricesFormModel
            {
                Rows = providers.Select(p => new KatshAirtimePriceRowFormModel
                {
                    Provider = p.Provider,
                    PriceUsd = p.PriceUsd,
                    PriceLbp = p.PriceLbp,
                    CostOneUsd = p.CostOneUsd,
                    CostOneLbp = p.CostOneLbp,
                    CostTwoUsd = p.CostTwoUsd,
                    CostTwoLbp = p.CostTwoLbp,
                    CostThreePlusUsd = p.CostThreePlusUsd,
                    CostThreePlusLbp = p.CostThreePlusLbp
                }).ToList()
            },
            SellForm = sellForm ?? new SellKatshAirtimeFormModel()
        };
    }

    private async Task<KatshAlfaPageViewModel> BuildAlfaPageAsync(CancellationToken cancellationToken) =>
        await BuildVoucherPageAsync(
            AlfaVouchers,
            (rate, ct) => _prices.GetAlfaPricesAsync(rate, ct),
            cancellationToken);

    private async Task<KatshAlfaPageViewModel> BuildTouchPageAsync(CancellationToken cancellationToken) =>
        await BuildVoucherPageAsync(
            TouchVouchers,
            (rate, ct) => _prices.GetTouchPricesAsync(rate, ct),
            cancellationToken);

    private async Task<KatshAlfaPageViewModel> BuildVoucherPageAsync(
        (string Label, string Image, string AmountKey, decimal FaceUsd)[] catalog,
        Func<decimal, CancellationToken, Task<IReadOnlyDictionary<string, KatshVoucherPrice>>> loadPrices,
        CancellationToken cancellationToken)
    {
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var lbpPerUsd = settings?.LbpPerUsd ?? 89_500m;
        var priceMap = await loadPrices(lbpPerUsd, cancellationToken);
        var canEdit = User.IsInRole(ApplicationRoles.Admin);
        var balance = await _balances.GetOpenSessionAsync(cancellationToken);

        var vouchers = catalog.Select(v =>
        {
            priceMap.TryGetValue(v.AmountKey, out var price);
            return new KatshAlfaVoucherCardViewModel
            {
                Label = v.Label,
                Image = v.Image,
                AmountKey = v.AmountKey,
                PriceUsd = price?.PriceUsd ?? v.FaceUsd,
                PriceLbp = price?.PriceLbp ?? 0m,
                CostUsd = price?.CostUsd ?? v.FaceUsd,
                CostLbp = price?.CostLbp ?? 0m
            };
        }).ToList();

        return new KatshAlfaPageViewModel
        {
            Vouchers = vouchers,
            CanEditPrices = canEdit,
            IsAccountsSessionOpen = await IsAccountsSessionOpenAsync(cancellationToken),
            BalanceUsd = balance?.BalanceUsd ?? 0m,
            BalanceLbp = balance?.BalanceLbp ?? 0m,
            EditForm = new KatshAlfaPricesFormModel
            {
                Rows = vouchers.Select(v => new KatshAlfaPriceRowFormModel
                {
                    AmountKey = v.AmountKey,
                    Label = v.Label,
                    PriceUsd = v.PriceUsd,
                    PriceLbp = v.PriceLbp,
                    CostUsd = v.CostUsd,
                    CostLbp = v.CostLbp
                }).ToList()
            }
        };
    }

    private async Task<string> ResolveActorDisplayAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return User.Identity?.Name ?? "Staff";

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(user.UserName))
            return user.UserName.Trim();

        return user.Email ?? "Staff";
    }
}
