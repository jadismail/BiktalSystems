namespace Biktal.WebMVC.Models;

public sealed class PosIndexViewModel
{
    public IReadOnlyList<PosProductDto> Catalog { get; init; } = [];

    public IReadOnlyList<PosCustomerDto> Customers { get; init; } = [];

    public decimal TaxRate { get; init; } = 0.09m;

    public string StoreName { get; init; } = "Biktal Systems";

    public string? ReceiptFooter { get; init; }

    public bool PricesTaxInclusive { get; init; } = true;

    public string CurrencyCode { get; init; } = "USD";

    public decimal LbpPerUsd { get; init; } = 89_500m;
}
