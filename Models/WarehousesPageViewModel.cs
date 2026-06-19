namespace Biktal.WebMVC.Models;

public sealed class WarehousesPageViewModel
{
    public IReadOnlyList<WarehouseRowViewModel> Warehouses { get; init; } = Array.Empty<WarehouseRowViewModel>();

    public AddWarehouseFormModel NewWarehouse { get; init; } = new();

    public bool OpenAddModal { get; init; }
}
