using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class SupplierRowViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Code { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string? Description { get; init; }

    public int SortOrder { get; init; }

    public int PurchaseOrderCount { get; init; }
}

public sealed class SuppliersPageViewModel
{
    public IReadOnlyList<SupplierRowViewModel> Suppliers { get; init; } = Array.Empty<SupplierRowViewModel>();

    public AddSupplierFormModel NewSupplier { get; init; } = new();

    public bool OpenAddModal { get; init; }
}

public sealed class AddSupplierFormModel
{
    [Required(ErrorMessage = "Supplier name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Supplier name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(32)]
    [Display(Name = "Code")]
    public string? Code { get; set; }

    [StringLength(64)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(256)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(512)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Sort order")]
    public int? SortOrder { get; set; }
}
