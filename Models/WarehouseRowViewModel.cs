namespace Biktal.WebMVC.Models;

public sealed class WarehouseRowViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Code { get; init; }

    public string? Description { get; init; }

    public int SortOrder { get; init; }
}
