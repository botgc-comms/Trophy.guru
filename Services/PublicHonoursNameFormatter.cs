using System.Text.RegularExpressions;

namespace Trophy.Catalogue.Services;

internal static partial class PublicHonoursNameFormatter
{
    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = Whitespace().Replace(value.Trim(), " ");
        result = TrailingPeriods().Replace(result, string.Empty).TrimEnd();
        return result.ToUpperInvariant();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\.+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingPeriods();
}
