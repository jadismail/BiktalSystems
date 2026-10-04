using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class PosCompleteSaleApiRequest
{
    public string? PaymentMethod { get; set; }

    public List<PosPaymentSplitApi> PaymentSplits { get; set; } = [];

    public decimal CashTendered { get; set; }

    public decimal Discount { get; set; }

    public decimal AmountReceived { get; set; }

    public string? PromoCode { get; set; }

    public PosCompleteSaleCustomerApi? Customer { get; set; }

    [MinLength(1)]
    public List<PosCompleteSaleLineApi> Lines { get; set; } = [];
}

public sealed class PosCompleteSaleCustomerApi
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }
}

public sealed class PosPaymentSplitApi
{
    public string? Method { get; set; }

    public decimal Amount { get; set; }
}

public sealed class PosCompleteSaleLineApi
{
    public string? ProductId { get; set; }

    public string? Sku { get; set; }

    public string? ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
