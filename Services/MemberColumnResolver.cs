using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Trophy.Catalogue.Services;

// Header-only matching: values such as phone numbers must never be used to guess a member ID.
internal static class MemberColumnResolver
{
    private static readonly string[][] Aliases =
    [
        ["full name", "member name", "display name", "name", "complete name", "name of member"],
        ["first name", "given name", "forename", "forenames", "given names", "christian name"],
        ["initial", "initials", "middle initial", "middle initials"],
        ["surname", "last name", "family name"],
        ["date of birth", "dob", "birth date", "birth year", "year of birth", "born", "birthday"],
        ["date joined", "join date", "joined date", "membership start date", "start date", "joined", "year joined", "joining date", "date of joining", "joining year", "member since", "membership since", "admission date"],
        ["membership number", "member number", "member login number", "membership no", "member no", "membership id", "member id", "member identifier", "membership identifier", "membership reference", "member reference", "membership ref", "member ref", "membership code", "member code", "login number", "login id"],
        ["gender", "sex", "member gender"]
    ];
    private static readonly string[] Labels =
        ["full name", "first name", "initials", "surname", "birth date", "joining date", "membership number", "gender"];

    public static int[] Resolve(IReadOnlyList<string> headers)
    {
        var candidates = headers.Select(Variants).ToArray();
        var result = Enumerable.Repeat(-1, Aliases.Length).ToArray();
        for (var field = 0; field < Aliases.Length; field++)
        {
            var aliases = Aliases[field].SelectMany(Variants).Distinct().ToArray();
            var ranked = candidates.Select((variants, column) => new
                {
                    Column = column,
                    Score = variants.SelectMany(value => aliases.Select(alias => Similarity(value, alias))).DefaultIfEmpty(0).Max()
                })
                .Where(candidate => candidate.Score >= 0.83)
                .OrderByDescending(candidate => candidate.Score).ToArray();
            if (ranked.Length == 0) continue;
            if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.04)
                throw new MemberImportException($"More than one column could be the {Labels[field]}: '{headers[ranked[0].Column]}' and '{headers[ranked[1].Column]}'. Rename the intended column and remove or rename the other before importing. The existing directory has not been changed.");
            result[field] = ranked[0].Column;
        }
        var duplicate = result.Where(column => column >= 0).GroupBy(column => column).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new MemberImportException($"The column '{headers[duplicate.Key]}' could describe more than one member field. Use a clearer heading before importing. The existing directory has not been changed.");
        return result;
    }

    private static string[] Variants(string header)
    {
        // Split camelCase as well as spaces/punctuation, remove accents and common presentation labels.
        var text = Regex.Replace(header, @"([a-z])([A-Z])", "$1 $2");
        text = new string(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant().Replace("#", " number ");
        var tokens = Regex.Matches(text, @"[a-z0-9]+").Select(match => match.Value)
            .Where(token => token is not ("optional" or "required" or "yyyy" or "yy" or "mm" or "dd"))
            .Select(token => token == "num" ? "number" : token).ToArray();
        var variants = new List<string> { string.Concat(tokens), string.Join(" ", tokens.Order()) };
        if (tokens.Length > 1 && tokens[0] is "member" or "members" or "personal")
        {
            var remainder = tokens.Skip(1).ToArray();
            variants.Add(string.Concat(remainder));
            variants.Add(string.Join(" ", remainder.Order()));
        }
        return variants.Where(value => value.Length > 0).Distinct().ToArray();
    }

    private static double Similarity(string value, string alias)
    {
        if (value == alias) return 1;
        // Never fuzzy-match short labels (ID, DOB, sex) or unrelated extra words.
        if (value.Length < 6 || alias.Length < 6 || Math.Abs(value.Length - alias.Length) > 2) return 0;
        var distance = new int[value.Length + 1, alias.Length + 1];
        for (var i = 0; i <= value.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= alias.Length; j++) distance[0, j] = j;
        for (var i = 1; i <= value.Length; i++)
        for (var j = 1; j <= alias.Length; j++)
        {
            distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                distance[i - 1, j - 1] + (value[i - 1] == alias[j - 1] ? 0 : 1));
            if (i > 1 && j > 1 && value[i - 1] == alias[j - 2] && value[i - 2] == alias[j - 1])
                distance[i, j] = Math.Min(distance[i, j], distance[i - 2, j - 2] + 1);
        }
        var edits = distance[value.Length, alias.Length];
        return edits <= 2 ? 1 - (double)edits / Math.Max(value.Length, alias.Length) : 0;
    }
}