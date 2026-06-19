namespace Biktal.WebMVC.Models;

public sealed class AccountIndexViewModel
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
