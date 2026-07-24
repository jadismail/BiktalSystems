using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class PosController : Controller
{
    private static readonly IReadOnlyList<PosProductDto> DemoCatalog =
    [
        new()
        {
            Id = "p1",
            Name = "iPhone 15 Pro 256GB",
            Sku = "PHONE-15P-256",
            Price = 1099m,
            Category = "Phones",
            Stock = 4
        },
        new()
        {
            Id = "p2",
            Name = "Samsung Galaxy S24 Ultra",
            Sku = "PHONE-S24U-512",
            Price = 1199m,
            Category = "Phones",
            Stock = 2
        },
        new()
        {
            Id = "p3",
            Name = "Google Pixel 9",
            Sku = "PHONE-P9-128",
            Price = 799m,
            Category = "Phones",
            Stock = 6
        },
        new()
        {
            Id = "a1",
            Name = "USB-C Fast Charger 35W",
            Sku = "ACC-UC35",
            Price = 29.99m,
            Category = "Accessories",
            Stock = 48
        },
        new()
        {
            Id = "a2",
            Name = "Tempered Glass (Universal)",
            Sku = "ACC-TG-UNI",
            Price = 12.50m,
            Category = "Accessories",
            Stock = 120
        },
        new()
        {
            Id = "a3",
            Name = "Silicone Case — Graphite",
            Sku = "ACC-CASE-G",
            Price = 24m,
            Category = "Accessories",
            Stock = 35
        },
        new()
        {
            Id = "a4",
            Name = "Wireless Earbuds Pro",
            Sku = "ACC-WEB-P",
            Price = 89m,
            Category = "Audio",
            Stock = 15
        },
        new()
        {
            Id = "a5",
            Name = "Power Bank 20,000mAh",
            Sku = "ACC-PB20",
            Price = 45m,
            Category = "Accessories",
            Stock = 22
        }
    ];

    private readonly ApplicationDbContext _db;
    private readonly PosSalePostingService _posSales;

    public PosController(ApplicationDbContext db, PosSalePostingService posSales)
    {
        _db = db;
        _posSales = posSales;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-page-pos";
        ViewData["HideFooter"] = true;

        var rows = await _db.CatalogProducts
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PosProductDto> catalog = rows.Count > 0
            ? rows.Select(static p => new PosProductDto
            {
                Id = p.Id.ToString("N"),
                Name = p.Name,
                Sku = p.Sku,
                Barcode = p.Barcode,
                Price = p.Price,
                Category = p.Category,
                Stock = p.StockQuantity
            }).ToList()
            : DemoCatalog;

        var customerRows = await _db.CrmCustomers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.FullName)
            .Select(c => new { c.Id, c.FullName, c.Email, c.Phone })
            .ToListAsync(cancellationToken);

        var customers = customerRows
            .Select(c => new PosCustomerDto
            {
                Id = c.Id.ToString("N"),
                Name = c.FullName,
                Email = c.Email ?? "",
                Phone = c.Phone ?? ""
            })
            .ToList();

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var vm = new PosIndexViewModel
        {
            Catalog = catalog,
            Customers = customers,
            TaxRate = settings?.DefaultTaxRate ?? 0.09m,
            StoreName = settings?.StoreDisplayName ?? "Biktal Systems",
            ReceiptFooter = settings?.ReceiptFooter,
            PricesTaxInclusive = settings?.PricesTaxInclusive ?? true,
            CurrencyCode = settings?.BaseCurrencyCode ?? "USD",
            LbpPerUsd = settings?.LbpPerUsd ?? 89_500m
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteSale(
        [FromBody] PosCompleteSaleApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            return BadRequest(new { error = "Cart is empty." });

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var taxRate = settings?.DefaultTaxRate ?? 0.09m;

        var checkout = new PosSaleCheckoutRequest
        {
            PaymentMethod = request.PaymentMethod,
            PaymentSplits = request.PaymentSplits
                .Select(s => new PosPaymentSplitRequest
                {
                    Method = s.Method,
                    Amount = s.Amount
                })
                .ToList(),
            CashTendered = request.CashTendered > 0m ? request.CashTendered : request.AmountReceived,
            Discount = request.Discount,
            TaxRate = taxRate,
            AmountReceived = request.CashTendered > 0m ? request.CashTendered : request.AmountReceived,
            PromoCode = request.PromoCode,
            Customer = request.Customer is null
                ? null
                : new PosSaleCustomerRequest
                {
                    Name = request.Customer.Name,
                    Email = request.Customer.Email,
                    Phone = request.Customer.Phone
                },
            Lines = request.Lines.Select(MapLine).ToList()
        };

        var result = await _posSales.CompleteSaleAsync(checkout, cancellationToken);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Json(new
        {
            saleNumber = result.SaleNumber,
            journalReference = result.JournalReference,
            journalEntryId = result.JournalEntryId,
            total = result.TotalAmount
        });
    }

    private static PosSaleLineRequest MapLine(PosCompleteSaleLineApi line)
    {
        Guid? productId = null;
        if (!string.IsNullOrWhiteSpace(line.ProductId))
        {
            if (Guid.TryParse(line.ProductId, out var g))
                productId = g;
            else if (Guid.TryParseExact(line.ProductId, "N", out g))
                productId = g;
            else if (Guid.TryParseExact(line.ProductId, "D", out g))
                productId = g;
        }

        return new PosSaleLineRequest
        {
            ProductId = productId,
            Sku = line.Sku,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        };
    }
}