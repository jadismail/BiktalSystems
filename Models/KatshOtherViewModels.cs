using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class KatshOtherPageViewModel
{
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public bool IsAccountsSessionOpen { get; init; }
    public CreateKatshManualSaleFormModel Form { get; set; } = new();

    public bool CanSell => IsAccountsSessionOpen;
}

public sealed class CreateKatshManualSaleFormModel
{
    [Required(ErrorMessage = "Enter a description.")]
    [StringLength(120, ErrorMessage = "Description must be 120 characters or fewer.")]
    [Display(Name = "Description")]
    public string Label { get; set; } = "";

    [Required(ErrorMessage = "Choose USD or LBP.")]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "USD";

    [Required(ErrorMessage = "Enter an amount.")]
    [Range(0.01, 100_000_000_000, ErrorMessage = "Amount must be greater than zero.")]
    [Display(Name = "Amount")]
    public decimal? Amount { get; set; }

    /// <summary>Customer payment: Cash or Whish.</summary>
    [Required(ErrorMessage = "Choose Cash or Whish.")]
    [Display(Name = "Payment")]
    public string PaymentMethod { get; set; } = "";
}
