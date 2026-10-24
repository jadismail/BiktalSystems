using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public sealed class KatshBalanceSession
{
    public DateOnly SessionDate { get; set; }
    public decimal BalanceUsd { get; set; }
    public decimal BalanceLbp { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public string? OpenedBy { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? ConfirmedBy { get; set; }
    public bool IsOpen { get; set; }
}

public enum KatshBalanceState
{
    /// <summary>No float has been opened yet — enter initial amounts.</summary>
    NotInitialized,

    /// <summary>Yesterday’s remaining float is ready; confirm before using Katsh today.</summary>
    NeedsDayConfirmation,

    /// <summary>Today’s float is confirmed and ready for sales.</summary>
    OpenToday
}

public sealed class KatshBalanceSnapshot
{
    public KatshBalanceState State { get; init; }
    public decimal BalanceUsd { get; init; }
    public decimal BalanceLbp { get; init; }
    public DateOnly? SessionDate { get; init; }
    public string? OpenedBy { get; init; }
    public DateTime? OpenedAtUtc { get; init; }
    public string? ConfirmedBy { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
}

public sealed class KatshBalanceOpenResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public KatshBalanceSession? Session { get; init; }

    public static KatshBalanceOpenResult Ok(KatshBalanceSession session) =>
        new() { Success = true, Session = session };

    public static KatshBalanceOpenResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Persistent Katsh float (USD + LBP). Remaining balances carry to the next day
/// and must be confirmed before selling.
/// </summary>
public sealed class KatshBalanceSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KatshBalanceSessionStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "katsh-balance-session.json");
    }

    public async Task<KatshBalanceSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return BuildSnapshot(await ReadAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceSession?> GetOpenSessionAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = await ReadAsync(cancellationToken);
            if (BuildSnapshot(session).State != KatshBalanceState.OpenToday)
                return null;

            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceOpenResult> OpenSessionAsync(
        decimal balanceUsd,
        decimal balanceLbp,
        string? openedBy,
        CancellationToken cancellationToken = default)
    {
        if (balanceUsd < 0m || balanceLbp < 0m)
            return KatshBalanceOpenResult.Fail("Balances cannot be negative.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var existing = await ReadAsync(cancellationToken);
            var state = BuildSnapshot(existing).State;

            if (state == KatshBalanceState.OpenToday)
                return KatshBalanceOpenResult.Fail("Katsh balances are already open for today.");

            if (state == KatshBalanceState.NeedsDayConfirmation)
                return KatshBalanceOpenResult.Fail("Confirm yesterday’s carried balance before opening a new one.");

            var now = DateTime.UtcNow;
            var actor = string.IsNullOrWhiteSpace(openedBy) ? null : openedBy.Trim();
            var session = new KatshBalanceSession
            {
                SessionDate = today,
                BalanceUsd = RoundUsd(balanceUsd),
                BalanceLbp = RoundLbp(balanceLbp),
                OpenedAtUtc = now,
                OpenedBy = actor,
                ConfirmedAtUtc = now,
                ConfirmedBy = actor,
                IsOpen = true
            };

            await WriteAsync(session, cancellationToken);
            return KatshBalanceOpenResult.Ok(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceOpenResult> ConfirmDayAsync(
        string? confirmedBy,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var session = await ReadAsync(cancellationToken);
            var state = BuildSnapshot(session).State;

            if (state == KatshBalanceState.NotInitialized || session is null)
                return KatshBalanceOpenResult.Fail("Open an initial Katsh balance first.");

            if (state == KatshBalanceState.OpenToday)
                return KatshBalanceOpenResult.Ok(session);

            var actor = string.IsNullOrWhiteSpace(confirmedBy) ? null : confirmedBy.Trim();
            session.SessionDate = today;
            session.ConfirmedAtUtc = DateTime.UtcNow;
            session.ConfirmedBy = actor;
            session.IsOpen = true;

            await WriteAsync(session, cancellationToken);
            return KatshBalanceOpenResult.Ok(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceOpenResult> SetBalancesAsync(
        decimal balanceUsd,
        decimal balanceLbp,
        CancellationToken cancellationToken = default)
    {
        if (balanceUsd < 0m || balanceLbp < 0m)
            return KatshBalanceOpenResult.Fail("Balances cannot be negative.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = await ReadAsync(cancellationToken);
            if (BuildSnapshot(session).State != KatshBalanceState.OpenToday || session is null)
                return KatshBalanceOpenResult.Fail("Confirm today’s Katsh balances before updating them.");

            session.BalanceUsd = RoundUsd(balanceUsd);
            session.BalanceLbp = RoundLbp(balanceLbp);
            await WriteAsync(session, cancellationToken);
            return KatshBalanceOpenResult.Ok(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceOpenResult> AddBalancesAsync(
        decimal amountUsd,
        decimal amountLbp,
        CancellationToken cancellationToken = default)
    {
        if (amountUsd < 0m || amountLbp < 0m)
            return KatshBalanceOpenResult.Fail("Add amounts cannot be negative.");

        if (amountUsd == 0m && amountLbp == 0m)
            return KatshBalanceOpenResult.Fail("Enter an amount to add.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = await ReadAsync(cancellationToken);
            if (BuildSnapshot(session).State != KatshBalanceState.OpenToday || session is null)
                return KatshBalanceOpenResult.Fail("Confirm today’s Katsh balances before adding to them.");

            session.BalanceUsd = RoundUsd(session.BalanceUsd + amountUsd);
            session.BalanceLbp = RoundLbp(session.BalanceLbp + amountLbp);
            await WriteAsync(session, cancellationToken);
            return KatshBalanceOpenResult.Ok(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KatshBalanceOpenResult> DeductAsync(
        string currency,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        var isUsd = string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase);
        var isLbp = string.Equals(currency, "LBP", StringComparison.OrdinalIgnoreCase);
        if (!isUsd && !isLbp)
            return KatshBalanceOpenResult.Fail("Choose USD or LBP.");

        if (amount <= 0m)
            return KatshBalanceOpenResult.Fail("Amount must be greater than zero.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = await ReadAsync(cancellationToken);
            if (BuildSnapshot(session).State != KatshBalanceState.OpenToday || session is null)
                return KatshBalanceOpenResult.Fail("Confirm today’s Katsh balances before selling.");

            if (isUsd)
            {
                var debit = RoundUsd(amount);
                if (session.BalanceUsd < debit)
                    return KatshBalanceOpenResult.Fail("Insufficient USD balance.");

                session.BalanceUsd = RoundUsd(session.BalanceUsd - debit);
            }
            else
            {
                var debit = RoundLbp(amount);
                if (session.BalanceLbp < debit)
                    return KatshBalanceOpenResult.Fail("Insufficient LBP balance.");

                session.BalanceLbp = RoundLbp(session.BalanceLbp - debit);
            }

            await WriteAsync(session, cancellationToken);
            return KatshBalanceOpenResult.Ok(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static decimal RoundUsd(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundLbp(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static KatshBalanceSnapshot BuildSnapshot(KatshBalanceSession? session)
    {
        if (session is null || !session.IsOpen)
        {
            return new KatshBalanceSnapshot { State = KatshBalanceState.NotInitialized };
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var state = session.SessionDate == today
            ? KatshBalanceState.OpenToday
            : session.SessionDate < today
                ? KatshBalanceState.NeedsDayConfirmation
                : KatshBalanceState.NotInitialized;

        return new KatshBalanceSnapshot
        {
            State = state,
            BalanceUsd = session.BalanceUsd,
            BalanceLbp = session.BalanceLbp,
            SessionDate = session.SessionDate,
            OpenedBy = session.OpenedBy,
            OpenedAtUtc = session.OpenedAtUtc,
            ConfirmedBy = session.ConfirmedBy,
            ConfirmedAtUtc = session.ConfirmedAtUtc
        };
    }

    private async Task<KatshBalanceSession?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<KatshBalanceSession>(stream, JsonOptions, cancellationToken);
    }

    private async Task WriteAsync(KatshBalanceSession session, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
    }
}
