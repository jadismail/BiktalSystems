using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class KatshAlfaPageViewModel
{
    public IReadOnlyList<KatshAlfaVoucherCardViewModel> Vouchers { get; init; } = [];
    public bool CanEditPrices { get; init; }
    public bool IsAccountsSessionOpen { get; init; }
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public KatshAlfaPricesFormModel EditForm { get; set; } = new();

    public bool CanSell => IsAccountsSessionOpen;
}

public sealed class KatshAlfaVoucherCardViewModel
{
    public string Label { get; init; } = "";
    public string Image { get; init; } = "";
    public string AmountKey { get; init; } = "";
    public decimal PriceUsd { get; init; }
    public decimal PriceLbp { get; init; }
    public decimal CostUsd { get; init; }
    public decimal CostLbp { get; init; }
}

public sealed class KatshAlfaPricesFormModel
{
    public List<KatshAlfaPriceRowFormModel> Rows { get; set; } = [];
}

public sealed class KatshAlfaPriceRowFormModel
{
    [Required]
    public string AmountKey { get; set; } = "";

    public string Label { get; set; } = "";

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

public sealed class SellKatshVoucherFormModel
{
    [Required]
    public string AmountKey { get; set; } = "";

    [Required]
    public string Currency { get; set; } = "";

    /// <summary>Customer payment: Cash or Whish.</summary>
    [Required]
    public string PaymentMethod { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
