using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshVoucherPrice
{
    public string AmountKey { get; set; } = "";

    /// <summary>Selling price shown to the customer.</summary>
    public decimal PriceUsd { get; set; }
    public decimal PriceLbp { get; set; }

    /// <summary>Cost deducted from the Katsh balance when sold.</summary>
    public decimal CostUsd { get; set; }
    public decimal CostLbp { get; set; }
}

public sealed class KatshVoucherPriceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyList<(string AmountKey, decimal FaceUsd)> AlfaCatalog =
    [
        ("3.03", 3.03m),
        ("4.5", 4.5m),
        ("7.58", 7.58m),
        ("10.0", 10.0m),
        ("15.15", 15.15m),
        ("22.73", 22.73m),
        ("77.28", 77.28m)
    ];

    private static readonly IReadOnlyList<(string AmountKey, decimal FaceUsd)> TouchCatalog =
    [
        ("sos-1.22", 1.22m),
        ("magic-1.67", 1.67m),
        ("magic-3.79", 3.79m),
        ("start-4.50", 4.50m),
        ("magic-4.50", 4.50m),
        ("smart-7.50", 7.50m),
        ("magic-7.58", 7.58m),
        ("magic-10.0", 10.0m),
        ("super-13.50", 13.50m),
        ("magic-15.15", 15.15m),
        ("magic-22.73", 22.73m),
        ("magic-77.28", 77.28m)
    ];

    private readonly string _alfaFilePath;
    private readonly string _touchFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshVoucherPriceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _alfaFilePath = Path.Combine(dir, "katsh-alfa-prices.json");
        _touchFilePath = Path.Combine(dir, "katsh-touch-prices.json");
    }

    public static IReadOnlyList<(string AmountKey, decimal FaceUsd)> GetAlfaCatalog() => AlfaCatalog;

    public static IReadOnlyList<(string AmountKey, decimal FaceUsd)> GetTouchCatalog() => TouchCatalog;

    public Task<IReadOnlyDictionary<string, KatshVoucherPrice>> GetAlfaPricesAsync(
        decimal lbpPerUsd,
        CancellationToken cancellationToken = default) =>
        GetPricesAsync(_alfaFilePath, AlfaCatalog, lbpPerUsd, cancellationToken);

    public Task SaveAlfaPricesAsync(
        IEnumerable<KatshVoucherPrice> prices,
        CancellationToken cancellationToken = default) =>
        SavePricesAsync(_alfaFilePath, AlfaCatalog, prices, cancellationToken);

    public Task<IReadOnlyDictionary<string, KatshVoucherPrice>> GetTouchPricesAsync(
        decimal lbpPerUsd,
        CancellationToken cancellationToken = default) =>
        GetPricesAsync(_touchFilePath, TouchCatalog, lbpPerUsd, cancellationToken);

    public Task SaveTouchPricesAsync(
        IEnumerable<KatshVoucherPrice> prices,
        CancellationToken cancellationToken = default) =>
        SavePricesAsync(_touchFilePath, TouchCatalog, prices, cancellationToken);

    private async Task<IReadOnlyDictionary<string, KatshVoucherPrice>> GetPricesAsync(
        string filePath,
        IReadOnlyList<(string AmountKey, decimal FaceUsd)> catalog,
        decimal lbpPerUsd,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var map = await ReadMapAsync(filePath, cancellationToken);
            var changed = false;

            foreach (var (amountKey, faceUsd) in catalog)
            {
                var faceLbp = RoundLbp(faceUsd * lbpPerUsd);

                if (!map.TryGetValue(amountKey, out var existing))
                {
                    map[amountKey] = new KatshVoucherPrice
                    {
                        AmountKey = amountKey,
                        PriceUsd = faceUsd,
                        PriceLbp = faceLbp,
                        CostUsd = faceUsd,
                        CostLbp = faceLbp
                    };
                    changed = true;
                    continue;
                }

                var normalized = Normalize(existing, faceUsd, faceLbp);
                if (!Same(existing, normalized))
                {
                    map[amountKey] = normalized;
                    changed = true;
                }
            }

            if (changed)
                await WriteMapAsync(filePath, catalog, map, cancellationToken);

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SavePricesAsync(
        string filePath,
        IReadOnlyList<(string AmountKey, decimal FaceUsd)> catalog,
        IEnumerable<KatshVoucherPrice> prices,
        CancellationToken cancellationToken)
    {
        var allowed = catalog.Select(x => x.AmountKey).ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, KatshVoucherPrice>(StringComparer.Ordinal);

        foreach (var price in prices)
        {
            if (!allowed.Contains(price.AmountKey))
                continue;

            map[price.AmountKey] = new KatshVoucherPrice
            {
                AmountKey = price.AmountKey,
                PriceUsd = Math.Max(0m, price.PriceUsd),
                PriceLbp = Math.Max(0m, price.PriceLbp),
                CostUsd = Math.Max(0m, price.CostUsd),
                CostLbp = Math.Max(0m, price.CostLbp)
            };
        }

        foreach (var (amountKey, faceUsd) in catalog)
        {
            if (map.ContainsKey(amountKey))
                continue;

            map[amountKey] = new KatshVoucherPrice
            {
                AmountKey = amountKey,
                PriceUsd = faceUsd,
                PriceLbp = 0m,
                CostUsd = faceUsd,
                CostLbp = 0m
            };
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteMapAsync(filePath, catalog, map, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static KatshVoucherPrice Normalize(
        KatshVoucherPrice existing,
        decimal faceUsd,
        decimal faceLbp)
    {
        var priceUsd = existing.PriceUsd > 0m ? existing.PriceUsd : faceUsd;
        var priceLbp = existing.PriceLbp;
        var costUsd = existing.CostUsd > 0m
            ? existing.CostUsd
            : (existing.PriceUsd > 0m ? existing.PriceUsd : faceUsd);
        var costLbp = existing.CostLbp > 0m
            ? existing.CostLbp
            : (existing.PriceLbp > 0m ? existing.PriceLbp : faceLbp);

        return new KatshVoucherPrice
        {
            AmountKey = existing.AmountKey,
            PriceUsd = priceUsd,
            PriceLbp = priceLbp,
            CostUsd = costUsd,
            CostLbp = costLbp
        };
    }

    private static bool Same(KatshVoucherPrice a, KatshVoucherPrice b) =>
        a.PriceUsd == b.PriceUsd
        && a.PriceLbp == b.PriceLbp
        && a.CostUsd == b.CostUsd
        && a.CostLbp == b.CostLbp;

    private static decimal RoundLbp(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static async Task<Dictionary<string, KatshVoucherPrice>> ReadMapAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return new Dictionary<string, KatshVoucherPrice>(StringComparer.Ordinal);

        await using var stream = File.OpenRead(filePath);
        var list = await JsonSerializer.DeserializeAsync<List<KatshVoucherPrice>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return list
            .Where(p => !string.IsNullOrWhiteSpace(p.AmountKey))
            .GroupBy(p => p.AmountKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
    }

    private static async Task WriteMapAsync(
        string filePath,
        IReadOnlyList<(string AmountKey, decimal FaceUsd)> catalog,
        Dictionary<string, KatshVoucherPrice> map,
        CancellationToken cancellationToken)
    {
        var ordered = catalog
            .Select(item => map.TryGetValue(item.AmountKey, out var price)
                ? price
                : new KatshVoucherPrice
                {
                    AmountKey = item.AmountKey,
                    PriceUsd = item.FaceUsd,
                    PriceLbp = 0m,
                    CostUsd = item.FaceUsd,
                    CostLbp = 0m
                })
            .ToList();

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, ordered, JsonOptions, cancellationToken);
    }
}
