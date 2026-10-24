using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biktal.WebMVC.Services;

public enum UsedDeviceKind
{
    Phone = 0,
    Accessory = 1
}

public enum UsedDeviceStatus
{
    InStock = 0,
    Reserved = 1,
    Sold = 2
}

public sealed class UsedDeviceRecord
{
    public Guid Id { get; set; }

    public UsedDeviceKind Kind { get; set; } = UsedDeviceKind.Phone;

    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string Barcode { get; set; } = string.Empty;

    /// <summary>Linked catalog SKU so the device appears and sells in POS.</summary>
    public Guid? CatalogProductId { get; set; }

    /// <summary>Battery health percent, e.g. 87. Null for accessories or unknown.</summary>
    public int? BatteryHealthPercent { get; set; }

    /// <summary>Storage label, e.g. 256GB.</summary>
    public string? Storage { get; set; }

    public decimal Price { get; set; }

    public decimal? Cost { get; set; }

    public string? PreviousUserName { get; set; }

    public DateOnly ReceivedDate { get; set; }

    /// <summary>Issues found or work done on the device.</summary>
    public string? IssuesOrChanges { get; set; }

    public string? SerialOrImei { get; set; }

    public string? Color { get; set; }

    public UsedDeviceStatus Status { get; set; } = UsedDeviceStatus.InStock;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ModifiedAtUtc { get; set; }
}

public sealed class UsedDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UsedDeviceStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "used-devices.json");
    }

    public async Task<IReadOnlyList<UsedDeviceRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllUnlockedAsync(cancellationToken);
            return list
                .OrderByDescending(d => d.ReceivedDate)
                .ThenByDescending(d => d.CreatedAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UsedDeviceRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllUnlockedAsync(cancellationToken);
            return list.FirstOrDefault(d => d.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> BarcodeExistsAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var norm = barcode.Trim().ToUpperInvariant();
        if (norm.Length == 0)
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllUnlockedAsync(cancellationToken);
            return list.Any(d =>
                string.Equals(d.Barcode, norm, StringComparison.OrdinalIgnoreCase)
                && (excludeId is null || d.Id != excludeId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(UsedDeviceRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllUnlockedAsync(cancellationToken);
            var idx = list.FindIndex(d => d.Id == record.Id);
            if (idx >= 0)
                list[idx] = record;
            else
                list.Add(record);

            await WriteAllUnlockedAsync(list, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await ReadAllUnlockedAsync(cancellationToken);
            var removed = list.RemoveAll(d => d.Id == id) > 0;
            if (removed)
                await WriteAllUnlockedAsync(list, cancellationToken);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<UsedDeviceRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        var list = await JsonSerializer.DeserializeAsync<List<UsedDeviceRecord>>(stream, JsonOptions, cancellationToken);
        return list ?? [];
    }

    private async Task WriteAllUnlockedAsync(List<UsedDeviceRecord> list, CancellationToken cancellationToken)
    {
        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions, cancellationToken);
        }

        File.Move(tmp, _filePath, overwrite: true);
    }
}
