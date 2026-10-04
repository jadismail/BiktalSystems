using System.Text.RegularExpressions;

namespace Biktal.WebMVC.Security;

/// <summary>
/// Stores open/close actor identity inside <c>OpenNotes</c>/<c>CloseNotes</c>
/// (CashboxSession has no dedicated actor columns in the domain model).
/// </summary>
public static partial class CashboxSessionActorNotes
{
    private static readonly Regex ActorLine = ActorLineRegex();

    public static string Stamp(string action, string actorDisplay, string? userNotes)
    {
        var stamp = $"{action} by {actorDisplay.Trim()}";
        if (string.IsNullOrWhiteSpace(userNotes))
            return stamp;

        return stamp + "\n" + userNotes.Trim();
    }

    public static string? ExtractActor(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var firstLine = notes.Split('\n', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstLine is null)
            return null;

        var match = ActorLine.Match(firstLine);
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    public static string? ExtractUserNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var parts = notes.Split('\n', 2, StringSplitOptions.None);
        if (parts.Length == 0)
            return null;

        if (ActorLine.IsMatch(parts[0].Trim()))
            return parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null;

        return notes.Trim();
    }

    [GeneratedRegex(@"^(Opened|Closed) by (?<name>.+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex ActorLineRegex();
}
