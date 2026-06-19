namespace Biktal.WebMVC.Models;

public sealed class PosProductDto
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string? Barcode { get; init; }

    public decimal Price { get; init; }

    public string Category { get; init; } = string.Empty;

    public int Stock { get; init; }
}
