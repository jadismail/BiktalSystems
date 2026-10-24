using Biktal.Domain.Inventory;
using Biktal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Services;

/// <summary>
/// Keeps used-device JSON records linked to catalog SKUs so POS can search and sell them.
/// </summary>
public sealed class UsedDeviceCatalogSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly UsedDeviceStore _usedDevices;

    public UsedDeviceCatalogSyncService(ApplicationDbContext db, UsedDeviceStore usedDevices)
    {
        _db = db;
        _usedDevices = usedDevices;
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _usedDevices.GetAllAsync(cancellationToken);
        foreach (var device in devices)
        {
            var before = device.CatalogProductId;
            await SyncOneAsync(device, cancellationToken);
            if (device.CatalogProductId != before)
                await _usedDevices.UpsertAsync(device, cancellationToken);
        }
    }

    public async Task SyncOneAsync(UsedDeviceRecord device, CancellationToken cancellationToken = default)
    {
        var categoryName = device.Kind == UsedDeviceKind.Phone ? "Used Phones" : "Used Accessories";
        await EnsureCategoryAsync(categoryName, cancellationToken);

        Guid? brandId = null;
        if (!string.IsNullOrWhiteSpace(device.Brand))
        {
            brandId = await _db.CatalogBrands.AsNoTracking()
                .Where(b => b.Name.ToUpper() == device.Brand.Trim().ToUpperInvariant())
                .Select(b => (Guid?)b.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        CatalogProduct? product = null;
        if (device.CatalogProductId is { } linkedId)
            product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == linkedId, cancellationToken);

        if (product is null && !string.IsNullOrWhiteSpace(device.Barcode))
        {
            product = await _db.CatalogProducts.FirstOrDefaultAsync(
                p => p.Barcode != null && p.Barcode.ToUpper() == device.Barcode.ToUpperInvariant(),
                cancellationToken);
        }

        var sku = BuildSku(device.Barcode);
        var catalogName = BuildCatalogName(device);
        var stock = device.Status == UsedDeviceStatus.Sold ? 0 : 1;
        var cost = device.Cost ?? 0m;

        if (product is null)
        {
            if (await _db.CatalogProducts.AnyAsync(p => p.Sku.ToUpper() == sku.ToUpperInvariant(), cancellationToken))
                sku = $"{sku}-{device.Id.ToString("N")[..6].ToUpperInvariant()}";

            product = new CatalogProduct
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = catalogName,
                Category = categoryName,
                BrandId = brandId,
                Barcode = device.Barcode,
                Cost = cost,
                Price = device.Price,
                StockQuantity = stock,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            _db.CatalogProducts.Add(product);
        }
        else
        {
            product.Sku = sku;
            product.Name = catalogName;
            product.Category = categoryName;
            product.BrandId = brandId;
            product.Barcode = device.Barcode;
            product.Cost = cost;
            product.Price = device.Price;
            product.StockQuantity = stock;
            product.ModifiedAtUtc = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        device.CatalogProductId = product.Id;
    }

    public async Task UnlinkAsync(UsedDeviceRecord device, CancellationToken cancellationToken = default)
    {
        if (device.CatalogProductId is not { } productId)
            return;

        var product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
            return;

        var poLineCount = await _db.PurchaseOrderLines.CountAsync(l => l.CatalogProductId == productId, cancellationToken);
        if (poLineCount > 0)
        {
            product.StockQuantity = 0;
            product.Barcode = null;
            product.ModifiedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var movements = await _db.InventoryMovements
            .Where(m => m.CatalogProductId == productId)
            .ToListAsync(cancellationToken);

        foreach (var movement in movements)
        {
            movement.ProductSku ??= product.Sku;
            movement.ProductName ??= product.Name;
            movement.CatalogProductId = null;
        }

        _db.CatalogProducts.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCategoryAsync(string name, CancellationToken cancellationToken)
    {
        var exists = await _db.CatalogCategories.AnyAsync(
            c => c.Name.ToUpper() == name.ToUpperInvariant(),
            cancellationToken);
        if (exists)
            return;

        var maxSort = await _db.CatalogCategories.AnyAsync(cancellationToken)
            ? await _db.CatalogCategories.MaxAsync(c => c.SortOrder, cancellationToken)
            : 0;

        _db.CatalogCategories.Add(new CatalogCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Synced from Inventory → Used devices for POS sales.",
            SortOrder = maxSort + 10,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSku(string barcode)
    {
        var code = (barcode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return code.Length <= 96 ? code : code[..96];
    }

    private static string BuildCatalogName(UsedDeviceRecord device)
    {
        var parts = new List<string> { device.Name.Trim() };
        if (!string.IsNullOrWhiteSpace(device.Storage))
            parts.Add(device.Storage.Trim());
        if (device.BatteryHealthPercent is int bh)
            parts.Add($"Batt {bh}%");
        return string.Join(" · ", parts);
    }
}
