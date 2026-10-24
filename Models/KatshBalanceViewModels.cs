using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class KatshIndexPageViewModel
{
    public bool IsBalanceOpen { get; init; }
    public bool IsAccountsSessionOpen { get; init; }
    public bool NeedsDayConfirmation { get; init; }
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public string? OpenedBy { get; init; }
    public IReadOnlyList<KatshSearchVoucherCardViewModel> SearchVouchers { get; init; } = [];

    public bool CanSell => IsBalanceOpen && IsAccountsSessionOpen;
}

public sealed class KatshSearchVoucherCardViewModel
{
    public string Provider { get; init; } = "";
    public string Label { get; init; } = "";
    public string ImageFolder { get; init; } = "";
    public string Image { get; init; } = "";
    public string AmountKey { get; init; } = "";
    public decimal FaceUsd { get; init; }
    public decimal PriceUsd { get; init; }
    public decimal PriceLbp { get; init; }
    public decimal CostUsd { get; init; }
    public decimal CostLbp { get; init; }
    public string SearchText { get; init; } = "";
}

public sealed class KatshBalancePageViewModel
{
    public bool IsBalanceOpen { get; init; }
    public bool NeedsDayConfirmation { get; init; }
    public bool NeedsInitialOpen { get; init; }
    public bool CanManageBalance { get; init; }
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public DateOnly? CarryFromDate { get; init; }
    public string? OpenedBy { get; init; }
    public string? ConfirmedBy { get; init; }
    public DateTime? OpenedAtLocal { get; init; }
    public OpenKatshBalanceFormModel OpenForm { get; set; } = new();
    public OpenKatshBalanceFormModel AdjustForm { get; set; } = new();
    public AddKatshBalanceFormModel AddForm { get; set; } = new();
}

public sealed class OpenKatshBalanceFormModel : IValidatableObject
{
    private const decimal MaxUsd = 999_999.99m;
    private const decimal MaxLbp = 100_000_000_000m;

    [Display(Name = "USD balance")]
    public decimal? BalanceUsd { get; set; }

    [Display(Name = "LBP balance")]
    public decimal? BalanceLbp { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var usd = BalanceUsd ?? 0m;
        var lbp = BalanceLbp ?? 0m;

        if (usd < 0m || usd > MaxUsd)
            yield return new ValidationResult($"USD balance must be between 0 and {MaxUsd:N2}.", new[] { nameof(BalanceUsd) });

        if (lbp < 0m || lbp > MaxLbp)
            yield return new ValidationResult($"LBP balance must be between 0 and {MaxLbp:N0}.", new[] { nameof(BalanceLbp) });
    }
}

public sealed class AddKatshBalanceFormModel : IValidatableObject
{
    private const decimal MaxUsd = 999_999.99m;
    private const decimal MaxLbp = 100_000_000_000m;

    [Display(Name = "Add USD")]
    public decimal? AmountUsd { get; set; }

    [Display(Name = "Add LBP")]
    public decimal? AmountLbp { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var usd = AmountUsd ?? 0m;
        var lbp = AmountLbp ?? 0m;

        if (usd < 0m || usd > MaxUsd)
            yield return new ValidationResult($"USD amount must be between 0 and {MaxUsd:N2}.", new[] { nameof(AmountUsd) });

        if (lbp < 0m || lbp > MaxLbp)
            yield return new ValidationResult($"LBP amount must be between 0 and {MaxLbp:N0}.", new[] { nameof(AmountLbp) });

        if (usd == 0m && lbp == 0m)
            yield return new ValidationResult("Enter at least one amount to add.");
    }
}
