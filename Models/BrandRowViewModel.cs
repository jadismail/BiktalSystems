namespace Biktal.WebMVC.Models;

public sealed class BrandRowViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int SortOrder { get; init; }

    public int ProductCount { get; init; }
}
