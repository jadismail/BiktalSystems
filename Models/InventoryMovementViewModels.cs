using System.ComponentModel.DataAnnotations;
using Biktal.Domain.Inventory;

namespace Biktal.WebMVC.Models;

public sealed class InventoryMovementRowViewModel
{
    public Guid Id { get; init; }

    public string MovementNumber { get; init; } = string.Empty;

    public InventoryMovementType Type { get; init; }

    public string TypeLabel { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public int QuantityDelta { get; init; }

    public decimal TotalCost { get; init; }

    public string? JournalReference { get; init; }

    public string? GlSkipReason { get; init; }
}

public sealed class InventoryMovementsPageViewModel
{
    public IReadOnlyList<InventoryMovementRowViewModel> RecentMovements { get; init; } =
        Array.Empty<InventoryMovementRowViewModel>();

    public IReadOnlyList<ProductPickOptionViewModel> Products { get; init; } =
        Array.Empty<ProductPickOptionViewModel>();

    public IReadOnlyList<WarehousePickOptionViewModel> Warehouses { get; init; } =
        Array.Empty<WarehousePickOptionViewModel>();

    public CreateInventoryMovementFormModel Form { get; init; } = new();
}

public sealed class ProductPickOptionViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Cost { get; init; }
}

public sealed class WarehousePickOptionViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class CreateInventoryMovementFormModel
{
    [Required]
    [Display(Name = "Movement type")]
    public InventoryMovementType Type { get; set; } = InventoryMovementType.CustomerReturn;

    [Required]
    [Display(Name = "Product")]
    public Guid CatalogProductId { get; set; }

    [Range(1, 99_999_999)]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;

    [Display(Name = "Unit cost (optional)")]
    public decimal? UnitCost { get; set; }

    [Display(Name = "From warehouse")]
    public Guid? FromWarehouseId { get; set; }

    [Display(Name = "To warehouse")]
    public Guid? ToWarehouseId { get; set; }

    [Display(Name = "Refund to customer (USD)")]
    public decimal? RefundAmount { get; set; }

    [StringLength(128)]
    [Display(Name = "Supplier / PO reference")]
    public string? SourceReference { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public static class InventoryMovementLabels
{
    public static string For(InventoryMovementType type) =>
        type switch
        {
            InventoryMovementType.Purchase => "Purchase (receive)",
            InventoryMovementType.Sale => "Sale",
            InventoryMovementType.CustomerReturn => "Customer return",
            InventoryMovementType.SupplierReturn => "Supplier return",
            InventoryMovementType.Transfer => "Transfer",
            InventoryMovementType.Damage => "Damage / write-off",
            InventoryMovementType.Adjustment => "Adjustment",
            _ => type.ToString()
        };
}
