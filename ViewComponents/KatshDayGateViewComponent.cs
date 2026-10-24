using Biktal.WebMVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.ViewComponents;

public sealed class KatshDayGateViewModel
{
    public bool NeedsConfirmation { get; init; }
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public DateOnly? FromDate { get; init; }
}

public sealed class KatshDayGateViewComponent : ViewComponent
{
    private readonly KatshBalanceSessionStore _balances;

    public KatshDayGateViewComponent(KatshBalanceSessionStore balances)
    {
        _balances = balances;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var snapshot = await _balances.GetSnapshotAsync(HttpContext.RequestAborted);
        if (snapshot.State != KatshBalanceState.NeedsDayConfirmation)
            return Content(string.Empty);

        return View(new KatshDayGateViewModel
        {
            NeedsConfirmation = true,
            BalanceUsd = snapshot.BalanceUsd,
            BalanceLbp = snapshot.BalanceLbp,
            FromDate = snapshot.SessionDate
        });
    }
}
