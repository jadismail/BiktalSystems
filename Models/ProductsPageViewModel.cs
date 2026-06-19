using Biktal.Domain.Inventory;

namespace Biktal.WebMVC.Models;

public sealed class ProductsPageViewModel
{
    public IReadOnlyList<CatalogProduct> Products { get; init; } = Array.Empty<CatalogProduct>();

    /// <summary>Average markup-on-cost % across products with cost &gt; 0; null when none qualify.</summary>
    public decimal? AverageMarginPercent { get; init; }

    /// <summary>Ordered rows from <see cref="CatalogCategory"/> — only these names may be chosen for new products.</summary>
    public IReadOnlyList<CatalogCategory> CategoryOptions { get; init; } = Array.Empty<CatalogCategory>();

    /// <summary>Ordered brands for optional product assignment.</summary>
    public IReadOnlyList<CatalogBrand> BrandOptions { get; init; } = Array.Empty<CatalogBrand>();

    public AddProductFormModel NewProduct { get; init; } = new();

    public EditProductFormModel? EditProduct { get; init; }

    /// <summary>When true (e.g. after a failed POST), the add-product modal should open on load.</summary>
    public bool OpenAddProductModal { get; init; }

    /// <summary>When true, the edit-product modal should open on load.</summary>
    public bool OpenEditProductModal { get; init; }
}
