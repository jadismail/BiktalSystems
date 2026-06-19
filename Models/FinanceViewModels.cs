using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Finance;

namespace Biktal.WebMVC.Models;

public static class GlAccountTypeLabels
{
    public static string Title(GlAccountType t) => t switch
    {
        GlAccountType.Asset => "Asset",
        GlAccountType.Liability => "Liability",
        GlAccountType.Equity => "Equity",
        GlAccountType.Revenue => "Revenue",
        GlAccountType.Expense => "Expense",
        _ => t.ToString()
    };
}

public sealed class FinanceIndexViewModel
{
    public int ActiveAccountCount { get; init; }

    public int JournalEntryCount { get; init; }

    public int JournalEntriesThisMonth { get; init; }

    public decimal ExpensesThisMonthTotal { get; init; }
}

public sealed class GlAccountRowViewModel
{
    public Guid Id { get; init; }

    public string AccountCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public GlAccountType Type { get; init; }

    public decimal PostedBalance { get; init; }

    public bool IsActive { get; init; }
}

public sealed class AccountsPageViewModel
{
    public IReadOnlyList<GlAccountRowViewModel> Accounts { get; init; } = Array.Empty<GlAccountRowViewModel>();

    public CreateGlAccountFormModel CreateForm { get; init; } = new();
}

public sealed class CreateGlAccountFormModel
{
    [Required(ErrorMessage = "Account code is required.")]
    [StringLength(32, MinimumLength = 1)]
    [Display(Name = "Account code")]
    public string AccountCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Account name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Account name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Account type")]
    public GlAccountType Type { get; set; } = GlAccountType.Expense;
}

public sealed class JournalListRowViewModel
{
    public Guid Id { get; init; }

    public DateOnly EntryDate { get; init; }

    public string Reference { get; init; } = string.Empty;

    public string? Memo { get; init; }

    public decimal TotalDebits { get; init; }
}

public sealed class JournalListPageViewModel
{
    public IReadOnlyList<JournalListRowViewModel> Entries { get; init; } = Array.Empty<JournalListRowViewModel>();
}

public sealed class JournalLineDetailViewModel
{
    public string AccountCode { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public string? LineMemo { get; init; }

    public decimal Debit { get; init; }

    public decimal Credit { get; init; }
}

public sealed class JournalEntryDetailViewModel
{
    public Guid Id { get; init; }

    public DateOnly EntryDate { get; init; }

    public string Reference { get; init; } = string.Empty;

    public string? Memo { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public IReadOnlyList<JournalLineDetailViewModel> Lines { get; init; } = Array.Empty<JournalLineDetailViewModel>();

    public decimal TotalDebits { get; init; }

    public decimal TotalCredits { get; init; }
}

public sealed class JournalLineFormRow
{
    public Guid? AccountId { get; set; }

    public string? Debit { get; set; }

    public string? Credit { get; set; }

    [StringLength(512)]
    public string? LineMemo { get; set; }
}

public sealed class GlAccountOptionViewModel
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

public sealed class NewJournalPageViewModel
{
    public required NewJournalEntryFormModel Form { get; init; }

    public required IReadOnlyList<GlAccountOptionViewModel> Accounts { get; init; }
}

public sealed class NewJournalEntryFormModel
{
    public const int LineSlotCount = 8;

    [Required]
    [Display(Name = "Entry date")]
    public DateOnly EntryDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [StringLength(2000)]
    public string? Memo { get; set; }

    public List<JournalLineFormRow> Lines { get; set; } =
        Enumerable.Range(0, LineSlotCount).Select(_ => new JournalLineFormRow()).ToList();
}

public sealed class FinanceExpenseRowViewModel
{
    public Guid Id { get; init; }

    public DateOnly ExpenseDate { get; init; }

    public string VendorName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string? Notes { get; init; }
}

public sealed class FinanceExpensesPageViewModel
{
    public IReadOnlyList<FinanceExpenseRowViewModel> Expenses { get; init; } = Array.Empty<FinanceExpenseRowViewModel>();

    public CreateFinanceExpenseFormModel CreateForm { get; init; } = new();
}

public sealed class CreateFinanceExpenseFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    [Required]
    [Display(Name = "Date")]
    public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Required(ErrorMessage = "Vendor is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Vendor / payee")]
    public string VendorName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Category")]
    public string Category { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Amount (USD)")]
    public decimal Amount { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount < 0m || Amount > MaxMoney)
            yield return new ValidationResult($"Amount must be between 0 and {MaxMoney:N2}.", new[] { nameof(Amount) });
    }
}

public sealed class CashboxPaymentAccountViewModel
{
    public string AccountCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal PostedBalance { get; init; }

    public decimal SessionSales { get; init; }
}

public sealed class CashboxActiveSessionViewModel
{
    public Guid Id { get; init; }

    public DateOnly SessionDate { get; init; }

    public DateTimeOffset OpenedAtUtc { get; init; }

    public decimal OpeningCashFloat { get; init; }

    public decimal OpeningWhishBalance { get; init; }

    public decimal CashSalesTotal { get; init; }

    public decimal WhishSalesTotal { get; init; }

    public decimal ExpectedCash { get; init; }

    public decimal ExpectedWhish { get; init; }

    public int SaleCount { get; init; }

    public string? OpenNotes { get; init; }
}

public sealed class CashboxClosedSessionRowViewModel
{
    public Guid Id { get; init; }

    public DateOnly SessionDate { get; init; }

    public DateTimeOffset OpenedAtUtc { get; init; }

    public DateTimeOffset ClosedAtUtc { get; init; }

    public decimal OpeningCashFloat { get; init; }

    public decimal OpeningWhishBalance { get; init; }

    public decimal ExpectedCash { get; init; }

    public decimal ExpectedWhish { get; init; }

    public decimal CountedCash { get; init; }

    public decimal CountedWhish { get; init; }

    public decimal CashVariance { get; init; }

    public decimal WhishVariance { get; init; }

    public int SaleCount { get; init; }
}

public sealed class OpenCashboxSessionFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    [Display(Name = "Opening cash in drawer (USD)")]
    public decimal OpeningCashFloat { get; set; }

    [Display(Name = "Opening Whish balance (USD)")]
    public decimal OpeningWhishBalance { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OpeningCashFloat < 0m || OpeningCashFloat > MaxMoney)
            yield return new ValidationResult($"Opening cash must be between 0 and {MaxMoney:N2}.", new[] { nameof(OpeningCashFloat) });

        if (OpeningWhishBalance < 0m || OpeningWhishBalance > MaxMoney)
            yield return new ValidationResult($"Opening Whish balance must be between 0 and {MaxMoney:N2}.", new[] { nameof(OpeningWhishBalance) });
    }
}

public sealed class CloseCashboxSessionFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    public Guid SessionId { get; set; }

    [Display(Name = "Counted cash in drawer (USD)")]
    public decimal CountedCash { get; set; }

    [Display(Name = "Counted Whish balance (USD)")]
    public decimal CountedWhish { get; set; }

    [StringLength(2000)]
    [Display(Name = "Close notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CountedCash < 0m || CountedCash > MaxMoney)
            yield return new ValidationResult($"Counted cash must be between 0 and {MaxMoney:N2}.", new[] { nameof(CountedCash) });

        if (CountedWhish < 0m || CountedWhish > MaxMoney)
            yield return new ValidationResult($"Counted Whish balance must be between 0 and {MaxMoney:N2}.", new[] { nameof(CountedWhish) });
    }
}

public sealed class CashboxPageViewModel
{
    public CashboxActiveSessionViewModel? ActiveSession { get; init; }

    public IReadOnlyList<CashboxPaymentAccountViewModel> PaymentAccounts { get; init; } =
        Array.Empty<CashboxPaymentAccountViewModel>();

    public IReadOnlyList<CashboxClosedSessionRowViewModel> RecentCloses { get; init; } =
        Array.Empty<CashboxClosedSessionRowViewModel>();

    public OpenCashboxSessionFormModel OpenForm { get; init; } = new();

    public CloseCashboxSessionFormModel CloseForm { get; init; } = new();
}
