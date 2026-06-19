namespace Biktal.WebMVC.Models;

public sealed class CategoriesPageViewModel
{
    public IReadOnlyList<CategoryRowViewModel> Categories { get; init; } = Array.Empty<CategoryRowViewModel>();

    public AddCategoryFormModel NewCategory { get; init; } = new();

    public bool OpenAddModal { get; init; }
}
