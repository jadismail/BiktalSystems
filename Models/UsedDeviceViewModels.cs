using System.ComponentModel.DataAnnotations;
using Biktal.WebMVC.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Biktal.WebMVC.Models;

public static class UsedDeviceKindLabels
{
    public static string Title(UsedDeviceKind kind) => kind switch
    {
        UsedDeviceKind.Phone => "Phone",
        UsedDeviceKind.Accessory => "Accessory",
        _ => kind.ToString()
    };
}

public static class UsedDeviceStatusLabels
{
    public static string Title(UsedDeviceStatus status) => status switch
    {
        UsedDeviceStatus.InStock => "In stock",
        UsedDeviceStatus.Reserved => "Reserved",
        UsedDeviceStatus.Sold => "Sold",
        _ => status.ToString()
    };
}

public sealed class UsedDeviceRowViewModel
{
    public Guid Id { get; init; }

    public UsedDeviceKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Brand { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public int? BatteryHealthPercent { get; init; }

    public string? Storage { get; init; }

    public decimal Price { get; init; }

    public string? PreviousUserName { get; init; }

    public DateOnly ReceivedDate { get; init; }

    public UsedDeviceStatus Status { get; init; }
}

public sealed class UsedDevicesPageViewModel
{
    public IReadOnlyList<UsedDeviceRowViewModel> Devices { get; init; } = Array.Empty<UsedDeviceRowViewModel>();

    public int InStockCount { get; init; }

    public int SoldCount { get; init; }

    public int ActiveCount { get; init; }

    public decimal InStockRetailValue { get; init; }
}

public sealed class UsedDeviceFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    public Guid? DeviceId { get; set; }

    [Required]
    [Display(Name = "Type")]
    public UsedDeviceKind Kind { get; set; } = UsedDeviceKind.Phone;

    [Required(ErrorMessage = "Device name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Name / model")]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }

    [Required(ErrorMessage = "Generate a barcode before saving.")]
    [StringLength(64, MinimumLength = 4)]
    [Display(Name = "Barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Range(0, 100, ErrorMessage = "Battery health must be between 0 and 100.")]
    [Display(Name = "Battery health (%)")]
    public int? BatteryHealthPercent { get; set; }

    [StringLength(64)]
    [Display(Name = "Storage")]
    public string? Storage { get; set; }

    [Required]
    [Range(0, (double)MaxMoney)]
    [Display(Name = "Selling price")]
    [DataType(DataType.Currency)]
    public decimal Price { get; set; }

    [Range(0, (double)MaxMoney)]
    [Display(Name = "Cost / buy price")]
    [DataType(DataType.Currency)]
    public decimal? Cost { get; set; }

    [StringLength(256)]
    [Display(Name = "Previous user")]
    public string? PreviousUserName { get; set; }

    [Required(ErrorMessage = "Received / bought date is required.")]
    [Display(Name = "Received / bought date")]
    [DataType(DataType.Date)]
    public DateOnly ReceivedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(4000)]
    [Display(Name = "Issues / changes")]
    public string? IssuesOrChanges { get; set; }

    [StringLength(128)]
    [Display(Name = "IMEI / serial")]
    public string? SerialOrImei { get; set; }

    [StringLength(64)]
    [Display(Name = "Color")]
    public string? Color { get; set; }

    [Required]
    [Display(Name = "Status")]
    public UsedDeviceStatus Status { get; set; } = UsedDeviceStatus.InStock;

    [ValidateNever]
    public IReadOnlyList<string> BrandOptions { get; set; } = Array.Empty<string>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Kind == UsedDeviceKind.Phone && BatteryHealthPercent is null)
        {
            yield return new ValidationResult(
                "Battery health is required for phones.",
                [nameof(BatteryHealthPercent)]);
        }

        if (Kind == UsedDeviceKind.Phone && string.IsNullOrWhiteSpace(Storage))
        {
            yield return new ValidationResult(
                "Storage is required for phones.",
                [nameof(Storage)]);
        }
    }
}

public sealed class UsedDeviceDetailViewModel
{
    public Guid Id { get; init; }

    public UsedDeviceKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Brand { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public int? BatteryHealthPercent { get; init; }

    public string? Storage { get; init; }

    public decimal Price { get; init; }

    public decimal? Cost { get; init; }

    public string? PreviousUserName { get; init; }

    public DateOnly ReceivedDate { get; init; }

    public string? IssuesOrChanges { get; init; }

    public string? SerialOrImei { get; init; }

    public string? Color { get; init; }

    public UsedDeviceStatus Status { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ModifiedAtUtc { get; init; }
}
