namespace Biktal.WebMVC.Models;

public sealed class StockRowViewModel
{
    public Guid ProductId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public string? Barcode { get; init; }

    public int StockQuantity { get; init; }
}
