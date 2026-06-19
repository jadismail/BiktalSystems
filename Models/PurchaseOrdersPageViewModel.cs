using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Inventory;

namespace Biktal.WebMVC.Models;

public sealed class PurchaseOrderListRowViewModel
{
    public Guid Id { get; init; }

    public string PoNumber { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    public PurchaseOrderStatus Status { get; init; }

    public int LineCount { get; init; }

    public decimal LineTotal { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class PurchaseOrdersPageViewModel
{
    public IReadOnlyList<PurchaseOrderListRowViewModel> Orders { get; init; } = Array.Empty<PurchaseOrderListRowViewModel>();

    /// <summary>Suppliers from the directory (optional pick when creating a PO).</summary>
    public IReadOnlyList<CatalogSupplierOption> SupplierOptions { get; init; } = Array.Empty<CatalogSupplierOption>();

    public CreatePurchaseOrderFormModel NewOrder { get; init; } = new();

    public bool OpenCreateModal { get; init; }
}

public sealed class CatalogSupplierOption
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Code { get; init; }
}

public sealed class CreatePurchaseOrderFormModel : IValidatableObject
{
    [Display(Name = "Supplier from directory")]
    public Guid? SupplierId { get; set; }

    [StringLength(256)]
    [Display(Name = "Or supplier name")]
    public string? SupplierName { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Expected date")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasId = SupplierId is Guid g && g != Guid.Empty;
        var hasName = !string.IsNullOrWhiteSpace(SupplierName);
        if (!hasId && !hasName)
            yield return new ValidationResult(
                "Select a supplier from the directory or type a supplier name.",
                new[] { nameof(SupplierId), nameof(SupplierName) });
    }
}
