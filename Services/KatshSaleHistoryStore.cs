using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshSaleRecord
{
    public Guid Id { get; set; }
    public DateTime SoldAtUtc { get; set; }
    public DateOnly SaleDate { get; set; }
    public string Provider { get; set; } = "";
    public string AmountKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string Currency { get; set; } = "";
    /// <summary>Customer charge (selling price) in <see cref="Currency"/>.</summary>
    public decimal Amount { get; set; }
    /// <summary>Float cost deducted in <see cref="Currency"/>. Zero for balance ledger rows.</summary>
    public decimal Cost { get; set; }
    /// <summary>Customer payment method: Cash or Whish. Empty for balance ledger rows.</summary>
    public string? PaymentMethod { get; set; }
    /// <summary>Accounts cashbox session this sale was attributed to (USD Cash/Whish).</summary>
    public Guid? CashboxSessionId { get; set; }
    public string? SoldBy { get; set; }

    public bool IsMerchandiseSale =>
        !string.Equals(Provider, "Balance", StringComparison.OrdinalIgnoreCase);
}

public sealed record KatshCashboxContribution(decimal CashUsd, decimal WhishUsd, int SaleCount);

public sealed class KatshSaleHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshSaleHistoryStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-sales.json");
    }

    public async Task AppendAsync(KatshSaleRecord sale, CancellationToken cancellationToken = default)
    {
        await AppendRangeAsync([sale], cancellationToken);
    }

    public async Task AppendRangeAsync(
        IEnumerable<KatshSaleRecord> sales,
        CancellationToken cancellationToken = default)
    {
        var batch = sales.ToList();
        if (batch.Count == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllAsync(cancellationToken);
            list.AddRange(batch);
            await WriteAllAsync(list, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KatshSaleRecord>> GetByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllAsync(cancellationToken);
            return list
                .Where(s => s.SaleDate == date)
                .OrderByDescending(s => s.SoldAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KatshSaleRecord>> GetByMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllAsync(cancellationToken);
            return list
                .Where(s => s.SaleDate.Year == year && s.SaleDate.Month == month)
                .OrderByDescending(s => s.SoldAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KatshSaleRecord>> GetInSaleDateRangeAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllAsync(cancellationToken);
            return list
                .Where(s => s.SaleDate >= startInclusive && s.SaleDate <= endInclusive)
                .OrderByDescending(s => s.SoldAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Convert a Katsh amount to USD for Insights / Accounts (USD cashbox).</summary>
    public static decimal ToUsd(decimal amount, string currency, decimal lbpPerUsd)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            return amount;

        if (lbpPerUsd <= 0m)
            return 0m;

        return amount / lbpPerUsd;
    }

    public async Task<IReadOnlyList<KatshSaleRecord>> GetByCashboxSessionAsync(
        Guid cashboxSessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllAsync(cancellationToken);
            return list
                .Where(s => s.CashboxSessionId == cashboxSessionId && s.IsMerchandiseSale)
                .OrderByDescending(s => s.SoldAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshCashboxContribution> GetCashboxContributionAsync(
        Guid cashboxSessionId,
        decimal lbpPerUsd,
        CancellationToken cancellationToken = default)
    {
        var sales = await GetByCashboxSessionAsync(cashboxSessionId, cancellationToken);
        decimal cash = 0m;
        decimal whish = 0m;
        foreach (var sale in sales)
        {
            var usd = ToUsd(sale.Amount, sale.Currency, lbpPerUsd);
            if (string.Equals(sale.PaymentMethod, "Whish", StringComparison.OrdinalIgnoreCase))
                whish += usd;
            else if (string.Equals(sale.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase))
                cash += usd;
        }

        return new KatshCashboxContribution(cash, whish, sales.Count);
    }

    private async Task<List<KatshSaleRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<KatshSaleRecord>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private async Task WriteAllAsync(List<KatshSaleRecord> list, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, list, JsonOptions, cancellationToken);
    }
}
