using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class KatshMobileCreditsPageViewModel
{
    public int TouchCredits { get; init; }
    public int AlfaCredits { get; init; }
    public bool CanEditPrices { get; init; }
    public bool IsAccountsSessionOpen { get; init; }
    public IReadOnlyList<KatshMobileCreditsProviderPriceViewModel> Providers { get; init; } = [];
    public KatshMobileCreditsPricesFormModel EditForm { get; set; } = new();
    public SellKatshMobileCreditsFormModel SellForm { get; set; } = new();
    public AddKatshMobileCreditsFormModel AddForm { get; set; } = new();

    public bool CanSell => IsAccountsSessionOpen;
}

public sealed class KatshMobileCreditsProviderPriceViewModel
{
    public string Provider { get; init; } = "";
    public decimal PriceUsd { get; init; }
    public decimal PriceLbp { get; init; }
    public decimal CostOneUsd { get; init; }
    public decimal CostOneLbp { get; init; }
    public decimal CostTwoUsd { get; init; }
    public decimal CostTwoLbp { get; init; }
    public decimal CostThreePlusUsd { get; init; }
    public decimal CostThreePlusLbp { get; init; }
}

public sealed class KatshMobileCreditsPricesFormModel
{
    public List<KatshMobileCreditsPriceRowFormModel> Rows { get; set; } = [];
}

public sealed class KatshMobileCreditsPriceRowFormModel
{
    [Required]
    public string Provider { get; set; } = "";

    [Range(0, 1_000_000)]
    [Display(Name = "Sell price / credit ($)")]
    public decimal PriceUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Sell price / credit (L.L)")]
    public decimal PriceLbp { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Cost / credit when selling 1 ($)")]
    public decimal CostOneUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Cost / credit when selling 1 (L.L)")]
    public decimal CostOneLbp { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Cost / credit when selling 2 ($)")]
    public decimal CostTwoUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Cost / credit when selling 2 (L.L)")]
    public decimal CostTwoLbp { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Cost / credit when selling 3+ ($)")]
    public decimal CostThreePlusUsd { get; set; }

    [Range(0, 100_000_000_000)]
    [Display(Name = "Cost / credit when selling 3+ (L.L)")]
    public decimal CostThreePlusLbp { get; set; }
}

public sealed class SellKatshMobileCreditsFormModel
{
    [Required(ErrorMessage = "Choose Touch or Alfa.")]
    [Display(Name = "Network")]
    public string Provider { get; set; } = "Touch";

    [Required(ErrorMessage = "Choose USD or LBP.")]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "USD";

    [Required(ErrorMessage = "Enter credits sold.")]
    [Range(1, 999, ErrorMessage = "Credits must be between 1 and 999.")]
    [Display(Name = "Credits")]
    public int? Credits { get; set; } = 1;

    /// <summary>Customer payment: Cash or Whish.</summary>
    [Required(ErrorMessage = "Choose Cash or Whish.")]
    [Display(Name = "Payment")]
    public string PaymentMethod { get; set; } = "";
}

public sealed class AddKatshMobileCreditsFormModel
{
    [Range(0, 1_000_000, ErrorMessage = "Touch credits must be between 0 and 1,000,000.")]
    [Display(Name = "Touch credits")]
    public int? TouchCredits { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Alfa credits must be between 0 and 1,000,000.")]
    [Display(Name = "Alfa credits")]
    public int? AlfaCredits { get; set; }
}
