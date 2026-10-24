using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshValidityDaysPrice
{
    public string Provider { get; set; } = "";
    public int Days { get; set; }
    public decimal PriceUsd { get; set; }
    public decimal PriceLbp { get; set; }
    public decimal CostUsd { get; set; }
    public decimal CostLbp { get; set; }

    public string AmountKey => MakeKey(Provider, Days);

    public static string MakeKey(string provider, int days) =>
        $"{provider.Trim()}:{days}".ToLowerInvariant();
}

/// <summary>
/// Selling price / cost for Validity Days packages (Touch / Alfa × duration).
/// Cost is deducted from the Katsh USD/LBP float on sale.
/// </summary>
public sealed class KatshValidityDaysPriceStore
{
    public static readonly string[] Providers = ["Touch", "Alfa"];
    public static readonly int[] Durations = [30, 60, 90, 180, 360];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshValidityDaysPriceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-validity-days-prices.json");
    }

    public async Task<IReadOnlyDictionary<string, KatshValidityDaysPrice>> GetPricesAsync(
        decimal lbpPerUsd,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var map = await ReadMapAsync(cancellationToken);
            var changed = false;

            foreach (var provider in Providers)
            {
                foreach (var days in Durations)
                {
                    var key = KatshValidityDaysPrice.MakeKey(provider, days);
                    if (map.ContainsKey(key))
                        continue;

                    map[key] = DefaultFor(provider, days, lbpPerUsd);
                    changed = true;
                }
            }

            if (changed)
                await WriteMapAsync(map, cancellationToken);

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePricesAsync(
        IEnumerable<KatshValidityDaysPrice> prices,
        CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, KatshValidityDaysPrice>(StringComparer.OrdinalIgnoreCase);

        foreach (var price in prices)
        {
            var provider = NormalizeProvider(price.Provider);
            if (provider is null || !Durations.Contains(price.Days))
                continue;

            var normalized = Normalize(provider, price.Days, price);
            map[normalized.AmountKey] = normalized;
        }

        foreach (var provider in Providers)
        {
            foreach (var days in Durations)
            {
                var key = KatshValidityDaysPrice.MakeKey(provider, days);
                if (map.ContainsKey(key))
                    continue;

                map[key] = DefaultFor(provider, days, 0m);
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteMapAsync(map, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string? NormalizeProvider(string? provider)
    {
        if (string.Equals(provider, "Touch", StringComparison.OrdinalIgnoreCase))
            return "Touch";
        if (string.Equals(provider, "Alfa", StringComparison.OrdinalIgnoreCase))
            return "Alfa";
        return null;
    }

    public static string LabelFor(int days) => $"{days} Days";

    private static KatshValidityDaysPrice DefaultFor(string provider, int days, decimal lbpPerUsd)
    {
        // Rough face value: ~$0.10 per day, rounded to cents.
        var defaultUsd = Math.Round(days * 0.10m, 2, MidpointRounding.AwayFromZero);
        var defaultLbp = lbpPerUsd > 0m
            ? Math.Round(defaultUsd * lbpPerUsd, 0, MidpointRounding.AwayFromZero)
            : 0m;

        return new KatshValidityDaysPrice
        {
            Provider = provider,
            Days = days,
            PriceUsd = defaultUsd,
            PriceLbp = defaultLbp,
            CostUsd = defaultUsd,
            CostLbp = defaultLbp
        };
    }

    private static KatshValidityDaysPrice Normalize(string provider, int days, KatshValidityDaysPrice price) =>
        new()
        {
            Provider = provider,
            Days = days,
            PriceUsd = Math.Max(0m, price.PriceUsd),
            PriceLbp = Math.Max(0m, price.PriceLbp),
            CostUsd = Math.Max(0m, price.CostUsd),
            CostLbp = Math.Max(0m, price.CostLbp)
        };

    private async Task<Dictionary<string, KatshValidityDaysPrice>> ReadMapAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, KatshValidityDaysPrice>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(_filePath);
        var list = await JsonSerializer.DeserializeAsync<List<ValidityDaysPriceDto>>(
            stream, JsonOptions, cancellationToken) ?? [];

        return list
            .Where(d => !string.IsNullOrWhiteSpace(d.Provider) && d.Days is > 0)
            .Select(d => Normalize(d.Provider!.Trim(), d.Days, new KatshValidityDaysPrice
            {
                Provider = d.Provider!,
                Days = d.Days,
                PriceUsd = d.PriceUsd ?? 0m,
                PriceLbp = d.PriceLbp ?? 0m,
                CostUsd = d.CostUsd ?? 0m,
                CostLbp = d.CostLbp ?? 0m
            }))
            .GroupBy(p => p.AmountKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteMapAsync(
        Dictionary<string, KatshValidityDaysPrice> map,
        CancellationToken cancellationToken)
    {
        var ordered = new List<KatshValidityDaysPrice>();
        foreach (var provider in Providers)
        {
            foreach (var days in Durations)
            {
                var key = KatshValidityDaysPrice.MakeKey(provider, days);
                ordered.Add(map.TryGetValue(key, out var price)
                    ? Normalize(provider, days, price)
                    : DefaultFor(provider, days, 0m));
            }
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, ordered, JsonOptions, cancellationToken);
    }

    private sealed class ValidityDaysPriceDto
    {
        public string? Provider { get; set; }
        public int Days { get; set; }
        public decimal? PriceUsd { get; set; }
        public decimal? PriceLbp { get; set; }
        public decimal? CostUsd { get; set; }
        public decimal? CostLbp { get; set; }
    }
}
