using Biktal.Infrastructure.Inventory;

namespace Biktal.WebMVC.Models;

public sealed class InventoryImportPageViewModel
{
    public CatalogProductExcelImportResult? LastResult { get; init; }
}
