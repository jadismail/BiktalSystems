using System.ComponentModel.DataAnnotations;
using Biktal.Application.Security;
using Biktal.Domain.Admin;
using Biktal.Infrastructure.Persistence;

namespace Biktal.WebMVC.Models;

public sealed class AdminIndexViewModel
{
    public string StoreDisplayName { get; init; } = "";

    public int BranchCount { get; init; }

    public int ActiveBranchCount { get; init; }

    public int UserCount { get; init; }

    public AdminBackupRecord? LastSuccessfulBackup { get; init; }
}

public sealed class AdminSettingsFormModel
{
    [Required]
    [StringLength(256)]
    [Display(Name = "Store display name")]
    public string StoreDisplayName { get; set; } = "";

    [Required]
    [StringLength(16)]
    [Display(Name = "Default locale")]
    public string DefaultLocale { get; set; } = "en-US";

    [StringLength(2000)]
    [Display(Name = "Receipt footer")]
    public string? ReceiptFooter { get; set; }
}

public sealed class AdminTaxFormModel
{
    [Range(0, 1)]
    [Display(Name = "Default tax rate")]
    public decimal DefaultTaxRate { get; set; }

    [Required]
    [StringLength(8)]
    [Display(Name = "Base currency")]
    public string BaseCurrencyCode { get; set; } = "USD";

    [Display(Name = "Prices entered tax-inclusive at POS")]
    public bool PricesTaxInclusive { get; set; }

    [Range(1, 10_000_000)]
    [Display(Name = "LBP per 1 USD")]
    public decimal LbpPerUsd { get; set; } = 89_500m;
}

public sealed class AdminBranchListRowViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public string Code { get; init; } = "";

    public string TimezoneId { get; init; } = "";

    public bool IsActive { get; init; }
}

public sealed class AdminBranchesPageViewModel
{
    public IReadOnlyList<AdminBranchListRowViewModel> Branches { get; init; } = [];
}

public sealed class AdminBranchFormModel
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(256)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(32)]
    public string Code { get; set; } = "";

    [Required]
    [StringLength(64)]
    [Display(Name = "Timezone")]
    public string TimezoneId { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;
}

public sealed class AdminUserListRowViewModel
{
    public string Id { get; init; } = "";

    public string Email { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public DateOnly? HireDate { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public DateTimeOffset? LastLoginAtUtc { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool IsLockedOut { get; init; }

    public bool IsCurrentUser { get; init; }

    public bool CanDelete { get; init; }
}

public sealed class AdminUsersPageViewModel
{
    public IReadOnlyList<AdminUserListRowViewModel> Users { get; init; } = [];

    public bool CanInviteViaRegister { get; init; }
}

public sealed class AdminEditUserFormModel
{
    public string UserId { get; set; } = "";

    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(256)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    [Display(Name = "Hire date")]
    [DataType(DataType.Date)]
    public DateOnly? HireDate { get; set; }

    public IList<string> SelectedRoles { get; set; } = [];

    public IReadOnlyList<string> AllRoles { get; set; } = ApplicationRoles.All;

    public bool CanAssignAdminRole { get; set; }

    public bool IsLockedOut { get; set; }

    public bool LockAccount { get; set; }

    public bool IsCurrentUser { get; set; }

    public bool CanDelete { get; set; }
}

public sealed class AdminResetUserPasswordFormModel
{
    public string UserId { get; set; } = "";

    public string Email { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class AdminCreateUserFormModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [StringLength(256)]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Display(Name = "Hire date")]
    [DataType(DataType.Date)]
    public DateOnly? HireDate { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";

    public IList<string> SelectedRoles { get; set; } = [ApplicationRoles.Cashier];

    public IReadOnlyList<string> AllRoles { get; set; } = ApplicationRoles.All;
}

public sealed class AdminBackupsPageViewModel
{
    public string BackupScheduleNotes { get; init; } = "";

    public AdminBackupRecord? LastSuccessfulBackup { get; init; }

    public IReadOnlyList<AdminBackupRecord> RecentBackups { get; init; } = [];
}

public sealed class AdminLogBackupFormModel
{
    [Required]
    [StringLength(256)]
    public string Label { get; set; } = "Manual backup";

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class AdminBackupScheduleFormModel
{
    [Required]
    [StringLength(2000)]
    public string BackupScheduleNotes { get; set; } = "";
}

public sealed class AdminDataResetRowViewModel
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string CountLabel { get; init; }

    public required string ConfirmMessage { get; init; }
}

public sealed class AdminDataResetPageViewModel
{
    public required BusinessDataResetCounts Counts { get; init; }

    public IReadOnlyList<AdminDataResetRowViewModel> Areas { get; init; } = [];
}

public sealed class AdminDataResetFormModel
{
    [Required]
    public string Area { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type RESET to confirm.")]
    [Display(Name = "Confirmation")]
    public string Confirmation { get; set; } = string.Empty;
}
