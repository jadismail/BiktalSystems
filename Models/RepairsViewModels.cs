using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Repairs;

namespace Biktal.WebMVC.Models;

public static class RepairTicketStatusLabels
{
    public static string Title(RepairTicketStatus s) => s switch
    {
        RepairTicketStatus.Intake => "Intake",
        RepairTicketStatus.Inspection => "Inspection",
        RepairTicketStatus.WaitingParts => "Waiting parts",
        RepairTicketStatus.InRepair => "In repair",
        RepairTicketStatus.ReadyForPickup => "Ready for pickup",
        RepairTicketStatus.Delivered => "Delivered",
        RepairTicketStatus.Cancelled => "Cancelled",
        _ => s.ToString()
    };
}

public sealed class RepairTicketCardViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string DeviceSummary { get; init; } = string.Empty;

    public RepairTicketStatus Status { get; init; }
}

public sealed class RepairTicketColumnViewModel
{
    public RepairTicketStatus Status { get; init; }

    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<RepairTicketCardViewModel> Tickets { get; init; } = Array.Empty<RepairTicketCardViewModel>();
}

public sealed class RepairTicketsPageViewModel
{
    public IReadOnlyList<RepairTicketColumnViewModel> Columns { get; init; } = Array.Empty<RepairTicketColumnViewModel>();

    public IReadOnlyList<RepairTicketCardViewModel> ClosedTickets { get; init; } = Array.Empty<RepairTicketCardViewModel>();
}

public sealed class RepairsIndexViewModel
{
    public int OpenRepairsCount { get; init; }

    public int DeliveredTicketsCount { get; init; }

    public int OpenPurchaseOrdersCount { get; init; }
}

public sealed class NewRepairTicketFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    [Required(ErrorMessage = "Customer name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Customer name")]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(256)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Device summary is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Device / model")]
    public string DeviceSummary { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "IMEI / serial")]
    public string? SerialOrImei { get; set; }

    [Required(ErrorMessage = "Issue description is required.")]
    [StringLength(4000, MinimumLength = 1)]
    [Display(Name = "Issue description")]
    public string IssueDescription { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Accessories left with device")]
    public string? AccessoriesNotes { get; set; }

    [Display(Name = "Customer quote (USD)")]
    public decimal? EstimatedPrice { get; set; }

    [Display(Name = "Parts cost (USD)")]
    public decimal? PartsCost { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EstimatedPrice is decimal p && (p < 0m || p > MaxMoney))
            yield return new ValidationResult($"Customer quote must be between 0 and {MaxMoney:N2}.", new[] { nameof(EstimatedPrice) });

        if (PartsCost is decimal c && (c < 0m || c > MaxMoney))
            yield return new ValidationResult($"Parts cost must be between 0 and {MaxMoney:N2}.", new[] { nameof(PartsCost) });

        if (EstimatedPrice is decimal price && PartsCost is decimal parts && parts > price)
            yield return new ValidationResult("Parts cost cannot exceed the customer quote.", new[] { nameof(PartsCost) });
    }
}

public sealed class RepairTicketDetailViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public RepairTicketStatus Status { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string DeviceSummary { get; init; } = string.Empty;

    public string? SerialOrImei { get; init; }

    public string IssueDescription { get; init; } = string.Empty;

    public string? AccessoriesNotes { get; init; }

    public decimal? EstimatedPrice { get; init; }

    public decimal? PartsCost { get; init; }

    public decimal? MarginAmount =>
        EstimatedPrice is { } rev && PartsCost is { } cost ? rev - cost : null;

    public decimal? MarginPercent =>
        MarginAmount is { } m && EstimatedPrice is { } rev && rev > 0m ? m / rev * 100m : null;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public bool CanDelete { get; init; }
}
