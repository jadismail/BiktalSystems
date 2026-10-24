using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshMobileCreditsProviderPrices
{
    public string Provider { get; set; } = "";

    /// <summary>Fixed selling price for 1 credit (same at every volume).</summary>
    public decimal PriceUsd { get; set; }
    public decimal PriceLbp { get; set; }

    /// <summary>Cost of 1 credit when selling exactly 1 credit (accounting only).</summary>
    public decimal CostOneUsd { get; set; }
    public decimal CostOneLbp { get; set; }

    /// <summary>Cost of 1 credit when selling exactly 2 credits.</summary>
    public decimal CostTwoUsd { get; set; }
    public decimal CostTwoLbp { get; set; }

    /// <summary>Cost of 1 credit when selling 3 or more credits.</summary>
    public decimal CostThreePlusUsd { get; set; }
    public decimal CostThreePlusLbp { get; set; }
}

/// <summary>
/// Selling price / cost tiers for Credits on Mobile (independent of Airtime prices).
/// Cost is for history/profit only — inventory is deducted from Touch/Alfa credit balances.
/// </summary>
public sealed class KatshMobileCreditsPriceStore
{
    public static readonly string[] Providers = KatshMobileCreditsBalanceStore.Providers;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshMobileCreditsPriceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-mobile-credits-prices.json");
    }

    public async Task<IReadOnlyDictionary<string, KatshMobileCreditsProviderPrices>> GetPricesAsync(
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
                if (map.ContainsKey(provider))
                    continue;

                map[provider] = DefaultFor(provider, lbpPerUsd);
                changed = true;
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
        IEnumerable<KatshMobileCreditsProviderPrices> prices,
        CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, KatshMobileCreditsProviderPrices>(StringComparer.OrdinalIgnoreCase);

        foreach (var price in prices)
        {
            var provider = Providers.FirstOrDefault(p =>
                string.Equals(p, price.Provider, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
                continue;

            map[provider] = Normalize(provider, price);
        }

        foreach (var provider in Providers)
        {
            if (map.ContainsKey(provider))
                continue;

            map[provider] = DefaultFor(provider, 0m);
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

    public static decimal ResolveUnitPrice(KatshMobileCreditsProviderPrices prices, bool isUsd) =>
        isUsd ? prices.PriceUsd : prices.PriceLbp;

    public static decimal ResolveUnitCost(KatshMobileCreditsProviderPrices prices, int credits, bool isUsd)
    {
        if (credits <= 0)
            return 0m;

        if (credits == 1)
            return isUsd ? prices.CostOneUsd : prices.CostOneLbp;

        if (credits == 2)
            return isUsd ? prices.CostTwoUsd : prices.CostTwoLbp;

        return isUsd ? prices.CostThreePlusUsd : prices.CostThreePlusLbp;
    }

    public static string TierLabel(int credits) =>
        credits switch
        {
            1 => "1 credit",
            2 => "2 credits",
            _ => "3+ credits"
        };

    private static KatshMobileCreditsProviderPrices DefaultFor(string provider, decimal lbpPerUsd)
    {
        const decimal defaultUsd = 1.00m;
        var defaultLbp = lbpPerUsd > 0m
            ? Math.Round(defaultUsd * lbpPerUsd, 0, MidpointRounding.AwayFromZero)
            : 0m;

        return new KatshMobileCreditsProviderPrices
        {
            Provider = provider,
            PriceUsd = defaultUsd,
            PriceLbp = defaultLbp,
            CostOneUsd = defaultUsd,
            CostOneLbp = defaultLbp,
            CostTwoUsd = defaultUsd,
            CostTwoLbp = defaultLbp,
            CostThreePlusUsd = defaultUsd,
            CostThreePlusLbp = defaultLbp
        };
    }

    private static KatshMobileCreditsProviderPrices Normalize(
        string provider,
        KatshMobileCreditsProviderPrices price) =>
        new()
        {
            Provider = provider,
            PriceUsd = Math.Max(0m, price.PriceUsd),
            PriceLbp = Math.Max(0m, price.PriceLbp),
            CostOneUsd = Math.Max(0m, price.CostOneUsd),
            CostOneLbp = Math.Max(0m, price.CostOneLbp),
            CostTwoUsd = Math.Max(0m, price.CostTwoUsd),
            CostTwoLbp = Math.Max(0m, price.CostTwoLbp),
            CostThreePlusUsd = Math.Max(0m, price.CostThreePlusUsd),
            CostThreePlusLbp = Math.Max(0m, price.CostThreePlusLbp)
        };

    private async Task<Dictionary<string, KatshMobileCreditsProviderPrices>> ReadMapAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, KatshMobileCreditsProviderPrices>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(_filePath);
        var list = await JsonSerializer.DeserializeAsync<List<MobileCreditsPriceDto>>(
            stream, JsonOptions, cancellationToken) ?? [];

        return list
            .Where(d => !string.IsNullOrWhiteSpace(d.Provider))
            .Select(d => Normalize(d.Provider!, new KatshMobileCreditsProviderPrices
            {
                Provider = d.Provider!,
                PriceUsd = d.PriceUsd ?? 0m,
                PriceLbp = d.PriceLbp ?? 0m,
                CostOneUsd = d.CostOneUsd ?? 0m,
                CostOneLbp = d.CostOneLbp ?? 0m,
                CostTwoUsd = d.CostTwoUsd ?? 0m,
                CostTwoLbp = d.CostTwoLbp ?? 0m,
                CostThreePlusUsd = d.CostThreePlusUsd ?? 0m,
                CostThreePlusLbp = d.CostThreePlusLbp ?? 0m
            }))
            .GroupBy(p => p.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteMapAsync(
        Dictionary<string, KatshMobileCreditsProviderPrices> map,
        CancellationToken cancellationToken)
    {
        var ordered = Providers
            .Select(provider => map.TryGetValue(provider, out var price)
                ? Normalize(provider, price)
                : DefaultFor(provider, 0m))
            .ToList();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, ordered, JsonOptions, cancellationToken);
    }

    private sealed class MobileCreditsPriceDto
    {
        public string? Provider { get; set; }
        public decimal? PriceUsd { get; set; }
        public decimal? PriceLbp { get; set; }
        public decimal? CostOneUsd { get; set; }
        public decimal? CostOneLbp { get; set; }
        public decimal? CostTwoUsd { get; set; }
        public decimal? CostTwoLbp { get; set; }
        public decimal? CostThreePlusUsd { get; set; }
        public decimal? CostThreePlusLbp { get; set; }
    }
}
