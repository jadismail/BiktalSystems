using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class KatshValidityDaysPageViewModel
{
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public bool CanEditPrices { get; init; }
    public bool IsAccountsSessionOpen { get; init; }
    public IReadOnlyList<KatshValidityDaysOptionViewModel> Options { get; init; } = [];
    public KatshValidityDaysPricesFormModel EditForm { get; set; } = new();
    public SellKatshValidityDaysFormModel SellForm { get; set; } = new();

    public bool CanSell => IsAccountsSessionOpen;
}

public sealed class KatshValidityDaysOptionViewModel
{
    public string Provider { get; init; } = "";
    public int Days { get; init; }
    public string Label { get; init; } = "";
    public string AmountKey { get; init; } = "";
    public decimal PriceUsd { get; init; }
    public decimal PriceLbp { get; init; }
    public decimal CostUsd { get; init; }
    public decimal CostLbp { get; init; }
}

public sealed class KatshValidityDaysPricesFormModel
{
    public List<KatshValidityDaysPriceRowFormModel> Rows { get; set; } = [];
}

public sealed class KatshValidityDaysPriceRowFormModel
{
    [Required]
    public string Provider { get; set; } = "";

    [Range(1, 1000)]
    public int Days { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Price ($)")]
    public decimal PriceUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Price (L.L)")]
    public decimal PriceLbp { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Cost ($)")]
    public decimal CostUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Cost (L.L)")]
    public decimal CostLbp { get; set; }
}

public sealed class SellKatshValidityDaysFormModel
{
    [Required(ErrorMessage = "Choose Touch or Alfa.")]
    [Display(Name = "Network")]
    public string Provider { get; set; } = "Touch";

    [Required(ErrorMessage = "Choose a validity period.")]
    [Display(Name = "Validity")]
    public int? Days { get; set; } = 30;

    [Required(ErrorMessage = "Choose USD or LBP.")]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "USD";

    [Required(ErrorMessage = "Choose Cash or Whish.")]
    [Display(Name = "Payment")]
    public string PaymentMethod { get; set; } = "";
}
