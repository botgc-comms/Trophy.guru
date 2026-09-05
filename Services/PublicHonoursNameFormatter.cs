using System.Text.RegularExpressions;

namespace Trophy.Catalogue.Services;

internal static partial class PublicHonoursNameFormatter
{
    private static readonly HashSet<string> LowercaseSurnameParticles = new(StringComparer.Ordinal)
    {
        "da", "de", "del", "den", "der", "di", "dos", "du", "la", "le", "van", "von"
    };

    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = Whitespace().Replace(value.Trim(), " ");
        result = PeriodSpacing().Replace(result, ".");
        result = JoinedDottedInitial().Replace(result, " ");
        result = Whitespace().Replace(result, " ");
        result = StandaloneInitial().Replace(result, match => $"{match.Groups["initial"].Value.ToUpperInvariant()}.");
        result = NamePart().Replace(result, FormatNamePart);
        return Whitespace().Replace(result, " ").Trim();
    }

    private static string FormatNamePart(Match match)
    {
        var part = match.Value;
        if (part.Length == 1) return part.ToUpperInvariant();

        var lower = part.ToLowerInvariant();
        if (match.Index > 0 && part.All(char.IsLower) && LowercaseSurnameParticles.Contains(lower))
        {
            return lower;
        }

        if (lower.StartsWith("mc", StringComparison.Ordinal) && part.Length > 2)
        {
            var remainder = part[2..];
            return "Mc" + FormatOrdinaryPart(remainder);
        }

        return FormatOrdinaryPart(part);
    }

    private static string FormatOrdinaryPart(string part)
    {
        var hasUpper = part.Any(char.IsUpper);
        var hasLower = part.Any(char.IsLower);
        if (hasUpper && hasLower && char.IsUpper(part[0])) return part;

        var lower = part.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*\.\s*", RegexOptions.CultureInvariant)]
    private static partial Regex PeriodSpacing();

    [GeneratedRegex(@"(?<=[\p{L}\p{M}]\.)(?=[\p{L}\p{M}])", RegexOptions.CultureInvariant)]
    private static partial Regex JoinedDottedInitial();

    [GeneratedRegex(@"(?<![\p{L}\p{M}'’\-])(?<initial>[\p{L}])\.?(?=(?:\s|$))", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneInitial();

    [GeneratedRegex(@"[\p{L}\p{M}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NamePart();
}
