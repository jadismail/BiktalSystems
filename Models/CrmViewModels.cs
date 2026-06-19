using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Crm;

namespace Biktal.WebMVC.Models;

public static class CrmSupportTicketStatusLabels
{
    public static string Title(CrmSupportTicketStatus s) => s switch
    {
        CrmSupportTicketStatus.New => "New",
        CrmSupportTicketStatus.InProgress => "In progress",
        CrmSupportTicketStatus.WaitingCustomer => "Waiting on customer",
        CrmSupportTicketStatus.Resolved => "Resolved",
        CrmSupportTicketStatus.Closed => "Closed",
        _ => s.ToString()
    };
}

public static class CrmSupportTicketPriorityLabels
{
    public static string Title(CrmSupportTicketPriority p) => p switch
    {
        CrmSupportTicketPriority.Low => "Low",
        CrmSupportTicketPriority.Normal => "Normal",
        CrmSupportTicketPriority.High => "High",
        _ => p.ToString()
    };
}

public static class CrmCampaignStatusLabels
{
    public static string Title(CrmCampaignStatus s) => s switch
    {
        CrmCampaignStatus.Draft => "Draft",
        CrmCampaignStatus.Active => "Active",
        CrmCampaignStatus.Completed => "Completed",
        CrmCampaignStatus.Cancelled => "Cancelled",
        _ => s.ToString()
    };
}

public sealed class CrmIndexViewModel
{
    public int ActiveCustomerCount { get; init; }

    public int OpenSupportTicketCount { get; init; }

    public int ActiveCampaignCount { get; init; }

    public int TotalLoyaltyPointsOutstanding { get; init; }

    public decimal InstallmentOutstanding { get; init; }

    public int ActiveInstallmentPlanCount { get; init; }
}

public sealed class CrmCustomerListRowViewModel
{
    public Guid Id { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public int LoyaltyPoints { get; init; }

    public bool IsActive { get; init; }
}

public sealed class CrmCustomersPageViewModel
{
    public IReadOnlyList<CrmCustomerListRowViewModel> Customers { get; init; } = Array.Empty<CrmCustomerListRowViewModel>();
}

public sealed class CreateCrmCustomerFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(256)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(256)]
    [Display(Name = "Company")]
    public string? Company { get; set; }

    [StringLength(4000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Marketing opt-in")]
    public bool MarketingOptIn { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(Email))
            yield return new ValidationResult("Enter at least a phone number or an email.", new[] { nameof(Phone), nameof(Email) });
    }
}

public sealed class EditCrmCustomerFormModel : IValidatableObject
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(256)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(256)]
    [Display(Name = "Company")]
    public string? Company { get; set; }

    [StringLength(4000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Marketing opt-in")]
    public bool MarketingOptIn { get; set; }

    [Display(Name = "Loyalty points")]
    [Range(0, 9_999_999)]
    public int LoyaltyPoints { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(Email))
            yield return new ValidationResult("Enter at least a phone number or an email.", new[] { nameof(Phone), nameof(Email) });
    }
}

public sealed class CrmCustomerDetailPageViewModel
{
    public required EditCrmCustomerFormModel Form { get; init; }
}

public sealed class CrmLoyaltyRankRowViewModel
{
    public int Rank { get; init; }

    public Guid CustomerId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public int LoyaltyPoints { get; init; }
}

public sealed class CrmCustomerOptionViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class AdjustLoyaltyPointsFormModel : IValidatableObject
{
    [Required]
    [Display(Name = "Customer")]
    public Guid CustomerId { get; set; }

    [Required]
    [Display(Name = "Point change")]
    [Range(-50_000, 50_000)]
    public int Delta { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Delta == 0)
            yield return new ValidationResult("Enter a non-zero point change.", new[] { nameof(Delta) });
    }
}

public sealed class CrmLoyaltyPageViewModel
{
    public IReadOnlyList<CrmLoyaltyRankRowViewModel> Ranked { get; init; } = Array.Empty<CrmLoyaltyRankRowViewModel>();

    public IReadOnlyList<CrmCustomerOptionViewModel> CustomerOptions { get; init; } = Array.Empty<CrmCustomerOptionViewModel>();

    public AdjustLoyaltyPointsFormModel AdjustForm { get; init; } = new();
}

public sealed class CrmCampaignRowViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public CrmCampaignStatus Status { get; init; }
}

public sealed class CrmCampaignsPageViewModel
{
    public IReadOnlyList<CrmCampaignRowViewModel> Campaigns { get; init; } = Array.Empty<CrmCampaignRowViewModel>();

    public CreateCrmCampaignFormModel CreateForm { get; init; } = new();
}

public sealed class CreateCrmCampaignFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Campaign name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Campaign name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Channel is required.")]
    [StringLength(64, MinimumLength = 1)]
    [Display(Name = "Channel")]
    public string Channel { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    [StringLength(2000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Status")]
    public CrmCampaignStatus Status { get; set; } = CrmCampaignStatus.Draft;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate is DateOnly e && e < StartDate)
            yield return new ValidationResult("End date cannot be before start date.", new[] { nameof(EndDate) });
    }
}

public sealed class CrmSupportTicketListRowViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public string? CustomerName { get; init; }

    public string Subject { get; init; } = string.Empty;

    public CrmSupportTicketStatus Status { get; init; }

    public CrmSupportTicketPriority Priority { get; init; }
}

public sealed class CrmTicketsPageViewModel
{
    public IReadOnlyList<CrmSupportTicketListRowViewModel> Tickets { get; init; } = Array.Empty<CrmSupportTicketListRowViewModel>();
}

public sealed class CreateSupportTicketFormModel
{
    public Guid? CustomerId { get; set; }

    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(512, MinimumLength = 1)]
    [Display(Name = "Subject")]
    public string Subject { get; set; } = string.Empty;

    [StringLength(4000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Priority")]
    public CrmSupportTicketPriority Priority { get; set; } = CrmSupportTicketPriority.Normal;
}

public sealed class NewTicketPageViewModel
{
    public required CreateSupportTicketFormModel Form { get; init; }

    public required IReadOnlyList<CrmCustomerOptionViewModel> CustomerOptions { get; init; }
}

public sealed class SupportTicketDetailViewModel
{
    public Guid Id { get; init; }

    public string TicketNumber { get; init; } = string.Empty;

    public Guid? CustomerId { get; init; }

    public string? CustomerName { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string? Description { get; init; }

    public CrmSupportTicketStatus Status { get; init; }

    public CrmSupportTicketPriority Priority { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
