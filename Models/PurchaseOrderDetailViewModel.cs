using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Inventory;

namespace Biktal.WebMVC.Models;

public sealed class PurchaseOrderLineRowViewModel
{
    public Guid LineId { get; init; }

    public Guid CatalogProductId { get; init; }

    public string ProductSku { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public int QuantityOrdered { get; init; }

    public decimal UnitCost { get; init; }

    public int QuantityReceived { get; init; }

    public int Remaining => Math.Max(0, QuantityOrdered - QuantityReceived);

    public decimal LineTotal => QuantityOrdered * UnitCost;
}

public sealed class AddPurchaseOrderLineFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    [Required]
    [Display(Name = "Product")]
    public Guid CatalogProductId { get; set; }

    [Range(1, 99_999_999)]
    [Display(Name = "Quantity")]
    public int QuantityOrdered { get; set; } = 1;

    [Display(Name = "Unit cost (USD)")]
    public decimal UnitCost { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UnitCost < 0m || UnitCost > MaxMoney)
            yield return new ValidationResult($"Unit cost must be between 0 and {MaxMoney:N2}.", new[] { nameof(UnitCost) });
    }
}

public sealed class PurchaseOrderDetailViewModel
{
    public Guid PurchaseOrderId { get; init; }

    public string PoNumber { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    /// <summary>True when <see cref="PurchaseOrder.SupplierId"/> was set from the supplier directory.</summary>
    public bool UsesDirectorySupplier { get; init; }

    public string? SupplierDirectoryCode { get; init; }

    public PurchaseOrderStatus Status { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset? ExpectedAtUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public IReadOnlyList<PurchaseOrderLineRowViewModel> Lines { get; init; } = Array.Empty<PurchaseOrderLineRowViewModel>();

    public decimal OrderTotal => Lines.Sum(l => l.LineTotal);

    public IReadOnlyList<CatalogProductOption> ProductOptions { get; init; } = Array.Empty<CatalogProductOption>();

    public AddPurchaseOrderLineFormModel NewLine { get; init; } = new();

    public bool OpenAddLineModal { get; init; }
}

public sealed class CatalogProductOption
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
