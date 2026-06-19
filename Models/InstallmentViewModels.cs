using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Crm;

namespace Biktal.WebMVC.Models;

public static class InstallmentPlanStatusLabels
{
    public static string Title(InstallmentPlanStatus status) => status switch
    {
        InstallmentPlanStatus.Active => "Active",
        InstallmentPlanStatus.PaidOff => "Paid off",
        InstallmentPlanStatus.Defaulted => "Defaulted",
        InstallmentPlanStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string BadgeClass(InstallmentPlanStatus status) => status switch
    {
        InstallmentPlanStatus.Active => "text-bg-primary",
        InstallmentPlanStatus.PaidOff => "text-bg-success",
        InstallmentPlanStatus.Defaulted => "text-bg-danger",
        InstallmentPlanStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-light"
    };
}

public static class InstallmentScheduleItemStatusLabels
{
    public static string Title(InstallmentScheduleItemStatus status) => status switch
    {
        InstallmentScheduleItemStatus.Pending => "Pending",
        InstallmentScheduleItemStatus.Partial => "Partial",
        InstallmentScheduleItemStatus.Paid => "Paid",
        InstallmentScheduleItemStatus.Overdue => "Overdue",
        _ => status.ToString()
    };

    public static string BadgeClass(InstallmentScheduleItemStatus status) => status switch
    {
        InstallmentScheduleItemStatus.Pending => "text-bg-light border",
        InstallmentScheduleItemStatus.Partial => "text-bg-warning",
        InstallmentScheduleItemStatus.Paid => "text-bg-success",
        InstallmentScheduleItemStatus.Overdue => "text-bg-danger",
        _ => "text-bg-light"
    };
}

public sealed class InstallmentsIndexViewModel
{
    public decimal TotalOutstanding { get; init; }

    public int ActivePlanCount { get; init; }

    public int OverdueInstallmentCount { get; init; }

    public IReadOnlyList<InstallmentPlanListRowViewModel> Plans { get; init; } =
        Array.Empty<InstallmentPlanListRowViewModel>();
}

public sealed class InstallmentPlanListRowViewModel
{
    public Guid Id { get; init; }

    public string PlanNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public decimal BalanceDue { get; init; }

    public InstallmentPlanStatus Status { get; init; }

    public DateOnly? NextDueDate { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class NewInstallmentPageViewModel
{
    public IReadOnlyList<CrmCustomerOptionViewModel> Customers { get; init; } =
        Array.Empty<CrmCustomerOptionViewModel>();

    public CreateInstallmentPlanFormModel Form { get; init; } = new();
}

public sealed class CreateInstallmentPlanFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    [Required]
    [Display(Name = "Customer")]
    public Guid CrmCustomerId { get; set; }

    [Required]
    [StringLength(512, MinimumLength = 1)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Total amount (USD)")]
    public decimal TotalAmount { get; set; }

    [Display(Name = "Down payment (USD)")]
    public decimal DownPayment { get; set; }

    [Display(Name = "Number of installments")]
    [Range(1, 60)]
    public int InstallmentCount { get; set; } = 3;

    [Required]
    [Display(Name = "First due date")]
    public DateOnly FirstDueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);

    [Display(Name = "Down payment method")]
    public string DownPaymentMethod { get; set; } = "Cash";

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TotalAmount <= 0m || TotalAmount > MaxMoney)
            yield return new ValidationResult($"Total must be between 0.01 and {MaxMoney:N2}.", new[] { nameof(TotalAmount) });

        if (DownPayment < 0m || DownPayment >= TotalAmount)
            yield return new ValidationResult("Down payment must be less than the total.", new[] { nameof(DownPayment) });

        if (DownPayment > 0m && string.IsNullOrWhiteSpace(DownPaymentMethod))
            yield return new ValidationResult("Select a method for the down payment.", new[] { nameof(DownPaymentMethod) });
    }
}

public sealed class InstallmentPlanDetailViewModel
{
    public Guid Id { get; init; }

    public string PlanNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string? CustomerPhone { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public decimal DownPayment { get; init; }

    public decimal FinancedAmount { get; init; }

    public decimal AmountPaid { get; init; }

    public decimal BalanceDue { get; init; }

    public InstallmentPlanStatus Status { get; init; }

    public DateOnly StartDate { get; init; }

    public string? Notes { get; init; }

    public string? OpeningJournalReference { get; init; }

    public IReadOnlyList<InstallmentScheduleRowViewModel> Schedule { get; init; } =
        Array.Empty<InstallmentScheduleRowViewModel>();

    public IReadOnlyList<InstallmentPaymentRowViewModel> Payments { get; init; } =
        Array.Empty<InstallmentPaymentRowViewModel>();

    public RecordInstallmentPaymentFormModel PaymentForm { get; init; } = new();
}

public sealed class InstallmentScheduleRowViewModel
{
    public int Sequence { get; init; }

    public DateOnly DueDate { get; init; }

    public decimal AmountDue { get; init; }

    public decimal AmountPaid { get; init; }

    public decimal Remaining { get; init; }

    public InstallmentScheduleItemStatus Status { get; init; }
}

public sealed class InstallmentPaymentRowViewModel
{
    public DateTimeOffset ReceivedAtUtc { get; init; }

    public decimal Amount { get; init; }

    public string Method { get; init; } = string.Empty;

    public string? Notes { get; init; }

    public int? ScheduleSequence { get; init; }
}

public sealed class RecordInstallmentPaymentFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    public Guid PlanId { get; set; }

    [Display(Name = "Amount (USD)")]
    public decimal Amount { get; set; }

    [Required]
    [Display(Name = "Payment method")]
    public string Method { get; set; } = "Cash";

    [StringLength(512)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m || Amount > MaxMoney)
            yield return new ValidationResult($"Amount must be between 0.01 and {MaxMoney:N2}.", new[] { nameof(Amount) });
    }
}
