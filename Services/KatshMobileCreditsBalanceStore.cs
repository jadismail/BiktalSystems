using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshMobileCreditsBalances
{
    public int TouchCredits { get; set; }
    public int AlfaCredits { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class KatshMobileCreditsBalanceResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public KatshMobileCreditsBalances? Balances { get; init; }

    public static KatshMobileCreditsBalanceResult Ok(KatshMobileCreditsBalances balances) =>
        new() { Success = true, Balances = balances };

    public static KatshMobileCreditsBalanceResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Independent Touch / Alfa credit inventory for Credits on Mobile.
/// Not connected to the Katsh USD/LBP float.
/// </summary>
public sealed class KatshMobileCreditsBalanceStore
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

    public KatshMobileCreditsBalanceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-mobile-credits-balances.json");
    }

    public async Task<KatshMobileCreditsBalances> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public int GetProviderBalance(KatshMobileCreditsBalances balances, string provider) =>
        IsAlfa(provider) ? balances.AlfaCredits : balances.TouchCredits;

    public async Task<KatshMobileCreditsBalanceResult> AddAsync(
        int touchCredits,
        int alfaCredits,
        string? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (touchCredits < 0 || alfaCredits < 0)
            return KatshMobileCreditsBalanceResult.Fail("Credits to add cannot be negative.");

        if (touchCredits == 0 && alfaCredits == 0)
            return KatshMobileCreditsBalanceResult.Fail("Enter credits to add for Touch and/or Alfa.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadAsync(cancellationToken);
            current.TouchCredits = checked(current.TouchCredits + touchCredits);
            current.AlfaCredits = checked(current.AlfaCredits + alfaCredits);
            current.UpdatedAtUtc = DateTime.UtcNow;
            current.UpdatedBy = updatedBy;
            await WriteAsync(current, cancellationToken);
            return KatshMobileCreditsBalanceResult.Ok(current);
        }
        catch (OverflowException)
        {
            return KatshMobileCreditsBalanceResult.Fail("Credit balance is too large.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshMobileCreditsBalanceResult> SetAsync(
        int touchCredits,
        int alfaCredits,
        string? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (touchCredits < 0 || alfaCredits < 0)
            return KatshMobileCreditsBalanceResult.Fail("Balances cannot be negative.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = new KatshMobileCreditsBalances
            {
                TouchCredits = touchCredits,
                AlfaCredits = alfaCredits,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = updatedBy
            };
            await WriteAsync(current, cancellationToken);
            return KatshMobileCreditsBalanceResult.Ok(current);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshMobileCreditsBalanceResult> DeductAsync(
        string provider,
        int credits,
        CancellationToken cancellationToken = default)
    {
        if (credits < 1)
            return KatshMobileCreditsBalanceResult.Fail("Credits to deduct must be at least 1.");

        var normalized = NormalizeProvider(provider);
        if (normalized is null)
            return KatshMobileCreditsBalanceResult.Fail("Choose Touch or Alfa.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadAsync(cancellationToken);
            if (IsAlfa(normalized))
            {
                if (current.AlfaCredits < credits)
                    return KatshMobileCreditsBalanceResult.Fail(
                        $"Not enough Alfa credits (have {current.AlfaCredits}, need {credits}).");

                current.AlfaCredits -= credits;
            }
            else
            {
                if (current.TouchCredits < credits)
                    return KatshMobileCreditsBalanceResult.Fail(
                        $"Not enough Touch credits (have {current.TouchCredits}, need {credits}).");

                current.TouchCredits -= credits;
            }

            current.UpdatedAtUtc = DateTime.UtcNow;
            await WriteAsync(current, cancellationToken);
            return KatshMobileCreditsBalanceResult.Ok(current);
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

    private static bool IsAlfa(string provider) =>
        string.Equals(provider, "Alfa", StringComparison.OrdinalIgnoreCase);

    private async Task<KatshMobileCreditsBalances> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new KatshMobileCreditsBalances();

        await using var stream = File.OpenRead(_filePath);
        var dto = await JsonSerializer.DeserializeAsync<KatshMobileCreditsBalances>(
            stream, JsonOptions, cancellationToken);

        return dto ?? new KatshMobileCreditsBalances();
    }

    private async Task WriteAsync(KatshMobileCreditsBalances balances, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, balances, JsonOptions, cancellationToken);
    }
}
