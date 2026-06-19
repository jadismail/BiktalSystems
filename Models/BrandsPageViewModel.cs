namespace Biktal.WebMVC.Models;

public sealed class BrandsPageViewModel
{
    public IReadOnlyList<BrandRowViewModel> Brands { get; init; } = Array.Empty<BrandRowViewModel>();

    public AddBrandFormModel NewBrand { get; init; } = new();

    public bool OpenAddModal { get; init; }
}
