using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Staff;

namespace Biktal.WebMVC.Models;

public static class StaffPayrollRunStatusLabels
{
    public static string Title(StaffPayrollRunStatus s) => s switch
    {
        StaffPayrollRunStatus.Draft => "Draft",
        StaffPayrollRunStatus.Submitted => "Submitted",
        StaffPayrollRunStatus.Paid => "Paid",
        _ => s.ToString()
    };
}

public static class StaffActivityCategoryLabels
{
    public static string Title(StaffActivityCategory c) => c switch
    {
        StaffActivityCategory.Staff => "Staff & HR",
        StaffActivityCategory.Security => "Security",
        StaffActivityCategory.Finance => "Finance",
        StaffActivityCategory.Inventory => "Inventory",
        StaffActivityCategory.Crm => "CRM",
        StaffActivityCategory.System => "System",
        _ => c.ToString()
    };
}

public sealed class StaffIndexViewModel
{
    public int ActiveStaffCount { get; init; }

    public int OpenAttendanceSessionCount { get; init; }

    public int DraftPayrollRunCount { get; init; }

    public int ActivityEntriesLast7DaysCount { get; init; }
}

public sealed class StaffMemberListRowViewModel
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public string? WorkEmail { get; init; }

    public bool IsActive { get; init; }
}

public sealed class StaffTeamPageViewModel
{
    public IReadOnlyList<StaffMemberListRowViewModel> Members { get; init; } = Array.Empty<StaffMemberListRowViewModel>();
}

public sealed class CreateStaffMemberFormModel
{
    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Job title is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Job title")]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(256)]
    [EmailAddress]
    [Display(Name = "Work email")]
    public string? WorkEmail { get; set; }

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Employee code is required.")]
    [StringLength(32, MinimumLength = 2)]
    [Display(Name = "Employee code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Hire date")]
    [DataType(DataType.Date)]
    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "Hourly rate")]
    [Range(0, 9999.99)]
    public decimal? HourlyRate { get; set; }

    [StringLength(4000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public sealed class EditStaffMemberFormModel
{
    [Required]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Job title is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Job title")]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(256)]
    [EmailAddress]
    [Display(Name = "Work email")]
    public string? WorkEmail { get; set; }

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Employee code is required.")]
    [StringLength(32, MinimumLength = 2)]
    [Display(Name = "Employee code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Hire date")]
    [DataType(DataType.Date)]
    public DateOnly HireDate { get; set; }

    [Display(Name = "Termination date")]
    [DataType(DataType.Date)]
    public DateOnly? TerminationDate { get; set; }

    [Display(Name = "Hourly rate")]
    [Range(0, 9999.99)]
    public decimal? HourlyRate { get; set; }

    [StringLength(4000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public sealed class StaffMemberDetailPageViewModel
{
    public required EditStaffMemberFormModel Form { get; init; }
}

public sealed class StaffAttendanceOpenRowViewModel
{
    public Guid EntryId { get; init; }

    public Guid StaffMemberId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public DateTimeOffset ClockInUtc { get; init; }
}

public sealed class StaffAttendanceRecentRowViewModel
{
    public Guid EntryId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public DateTimeOffset ClockInUtc { get; init; }

    public DateTimeOffset? ClockOutUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed class StaffAttendancePageViewModel
{
    public IReadOnlyList<StaffAttendanceOpenRowViewModel> OpenSessions { get; init; } = Array.Empty<StaffAttendanceOpenRowViewModel>();

    public IReadOnlyList<StaffAttendanceRecentRowViewModel> RecentEntries { get; init; } = Array.Empty<StaffAttendanceRecentRowViewModel>();

    public IReadOnlyList<StaffMemberPickOptionViewModel> ActiveMembersForClockIn { get; init; } =
        Array.Empty<StaffMemberPickOptionViewModel>();

    public StaffClockInFormModel ClockInForm { get; init; } = new();
}

public sealed class StaffMemberPickOptionViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class StaffClockInFormModel
{
    [Required]
    [Display(Name = "Team member")]
    public Guid StaffMemberId { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes (optional)")]
    public string? Notes { get; set; }
}

public sealed class StaffClockOutFormModel
{
    [Required]
    public Guid AttendanceEntryId { get; set; }
}

public sealed class StaffPayrollRunListRowViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;

    public DateOnly PeriodStart { get; init; }

    public DateOnly PeriodEnd { get; init; }

    public StaffPayrollRunStatus Status { get; init; }

    public int LineCount { get; init; }

    public decimal TotalNetPay { get; init; }
}

public sealed class StaffPayrollListPageViewModel
{
    public IReadOnlyList<StaffPayrollRunListRowViewModel> Runs { get; init; } = Array.Empty<StaffPayrollRunListRowViewModel>();

    public CreateStaffPayrollRunFormModel NewRunForm { get; init; } = new();
}

public sealed class CreateStaffPayrollRunFormModel
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    [Display(Name = "Label")]
    public string Label { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Period start")]
    [DataType(DataType.Date)]
    public DateOnly PeriodStart { get; set; }

    [Required]
    [Display(Name = "Period end")]
    [DataType(DataType.Date)]
    public DateOnly PeriodEnd { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public sealed class StaffPayrollLineEditRowViewModel
{
    public Guid LineId { get; init; }

    public string MemberName { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public decimal GrossPay { get; init; }

    public decimal Deductions { get; init; }

    public decimal NetPay { get; init; }
}

public sealed class StaffPayrollRunDetailPageViewModel
{
    public Guid RunId { get; init; }

    public string Label { get; init; } = string.Empty;

    public DateOnly PeriodStart { get; init; }

    public DateOnly PeriodEnd { get; init; }

    public StaffPayrollRunStatus Status { get; init; }

    public string? Notes { get; init; }

    public bool CanEditLines { get; init; }

    public bool CanAdvanceStatus { get; init; }

    public IReadOnlyList<StaffPayrollLineEditRowViewModel> Lines { get; init; } = Array.Empty<StaffPayrollLineEditRowViewModel>();
}

public sealed class UpdateStaffPayrollLineFormModel
{
    [Required]
    public Guid LineId { get; set; }

    [Range(typeof(decimal), "0", "999999.99")]
    [Display(Name = "Gross pay")]
    public decimal GrossPay { get; set; }

    [Range(typeof(decimal), "0", "999999.99")]
    [Display(Name = "Deductions")]
    public decimal Deductions { get; set; }
}

public sealed class StaffActivityRowViewModel
{
    public DateTimeOffset OccurredAtUtc { get; init; }

    public string ActorDisplayName { get; init; } = string.Empty;

    public StaffActivityCategory Category { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid? RelatedStaffMemberId { get; init; }

    public string? RelatedMemberName { get; init; }
}

public sealed class StaffActivityPageViewModel
{
    public IReadOnlyList<StaffActivityRowViewModel> Entries { get; init; } = Array.Empty<StaffActivityRowViewModel>();
}
