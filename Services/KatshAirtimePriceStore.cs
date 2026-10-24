using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshAirtimeProviderPrices
{
    public string Provider { get; set; } = "";

    /// <summary>Fixed selling price for 1 credit (same at every volume).</summary>
    public decimal PriceUsd { get; set; }
    public decimal PriceLbp { get; set; }

    /// <summary>Cost of 1 credit when selling exactly 1 credit.</summary>
    public decimal CostOneUsd { get; set; }
    public decimal CostOneLbp { get; set; }

    /// <summary>Cost of 1 credit when selling exactly 2 credits.</summary>
    public decimal CostTwoUsd { get; set; }
    public decimal CostTwoLbp { get; set; }

    /// <summary>Cost of 1 credit when selling 3 or more credits.</summary>
    public decimal CostThreePlusUsd { get; set; }
    public decimal CostThreePlusLbp { get; set; }
}

public sealed class KatshAirtimePriceStore
{
    public static readonly string[] Providers = ["Touch", "Alfa"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshAirtimePriceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-airtime-prices.json");
    }

    public async Task<IReadOnlyDictionary<string, KatshAirtimeProviderPrices>> GetPricesAsync(
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
        IEnumerable<KatshAirtimeProviderPrices> prices,
        CancellationToken cancellationToken = default)
    {
        var allowed = Providers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, KatshAirtimeProviderPrices>(StringComparer.OrdinalIgnoreCase);

        foreach (var price in prices)
        {
            var provider = Providers.FirstOrDefault(p =>
                string.Equals(p, price.Provider, StringComparison.OrdinalIgnoreCase));
            if (provider is null || !allowed.Contains(provider))
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

    public static decimal ResolveUnitPrice(KatshAirtimeProviderPrices prices, bool isUsd) =>
        isUsd ? prices.PriceUsd : prices.PriceLbp;

    public static decimal ResolveUnitCost(KatshAirtimeProviderPrices prices, int credits, bool isUsd)
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

    private static KatshAirtimeProviderPrices DefaultFor(string provider, decimal lbpPerUsd)
    {
        const decimal defaultUsd = 1.00m;
        var defaultLbp = lbpPerUsd > 0m
            ? Math.Round(defaultUsd * lbpPerUsd, 0, MidpointRounding.AwayFromZero)
            : 0m;

        return new KatshAirtimeProviderPrices
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

    private static KatshAirtimeProviderPrices Normalize(string provider, KatshAirtimeProviderPrices price) =>
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

    private async Task<Dictionary<string, KatshAirtimeProviderPrices>> ReadMapAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, KatshAirtimeProviderPrices>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(_filePath);
        var list = await JsonSerializer.DeserializeAsync<List<AirtimePriceDto>>(
            stream, JsonOptions, cancellationToken) ?? [];

        return list
            .Select(MapDto)
            .Where(p => !string.IsNullOrWhiteSpace(p.Provider))
            .GroupBy(p => p.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private static KatshAirtimeProviderPrices MapDto(AirtimePriceDto dto)
    {
        // Migrate legacy "one/two/threePlus credit" fields that used to mean selling price tiers.
        var legacyOneUsd = dto.OneCreditUsd ?? 0m;
        var legacyOneLbp = dto.OneCreditLbp ?? 0m;
        var legacyTwoUsd = dto.TwoCreditsUsd ?? legacyOneUsd;
        var legacyTwoLbp = dto.TwoCreditsLbp ?? legacyOneLbp;
        var legacyThreeUsd = dto.ThreePlusCreditsUsd ?? legacyOneUsd;
        var legacyThreeLbp = dto.ThreePlusCreditsLbp ?? legacyOneLbp;

        return Normalize(dto.Provider ?? "", new KatshAirtimeProviderPrices
        {
            Provider = dto.Provider ?? "",
            PriceUsd = dto.PriceUsd ?? legacyOneUsd,
            PriceLbp = dto.PriceLbp ?? legacyOneLbp,
            CostOneUsd = dto.CostOneUsd ?? legacyOneUsd,
            CostOneLbp = dto.CostOneLbp ?? legacyOneLbp,
            CostTwoUsd = dto.CostTwoUsd ?? legacyTwoUsd,
            CostTwoLbp = dto.CostTwoLbp ?? legacyTwoLbp,
            CostThreePlusUsd = dto.CostThreePlusUsd ?? legacyThreeUsd,
            CostThreePlusLbp = dto.CostThreePlusLbp ?? legacyThreeLbp
        });
    }

    private async Task WriteMapAsync(
        Dictionary<string, KatshAirtimeProviderPrices> map,
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

    private sealed class AirtimePriceDto
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

        // Legacy tiered "price" fields
        public decimal? OneCreditUsd { get; set; }
        public decimal? OneCreditLbp { get; set; }
        public decimal? TwoCreditsUsd { get; set; }
        public decimal? TwoCreditsLbp { get; set; }
        public decimal? ThreePlusCreditsUsd { get; set; }
        public decimal? ThreePlusCreditsLbp { get; set; }
    }
}
