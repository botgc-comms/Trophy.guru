using System.Globalization;
using System.Text;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class FuzzyMemberMatcher
{
    public MemberMatchRecord? FindBest(WinnerRecord winner, IReadOnlyList<MemberRecord> members)
    {
        var inscription = NameParts.From(winner.Name);
        if (inscription.Surname.Length == 0) return null;

        var candidates = members
            .Where(member => !winner.RejectedMemberIds.Contains(member.Id, StringComparer.OrdinalIgnoreCase))
            .Select(member => Score(winner, inscription, member))
            .Where(candidate => candidate is not null)
            .Cast<ScoredMember>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Member.FullName)
            .Take(3)
            .ToList();
        var best = candidates.FirstOrDefault();
        if (best is null || best.Score < 0.68) return null;

        var margin = candidates.Count > 1 ? best.Score - candidates[1].Score : best.Score;
        var status = best.Score >= 0.9 && margin >= 0.08 ? MemberMatchStates.Strong : MemberMatchStates.Possible;
        return new MemberMatchRecord
        {
            MemberId = best.Member.Id,
            MemberName = best.Member.FullName,
            MembershipNumber = best.Member.MembershipNumber,
            BirthYear = best.Member.BirthYear,
            Confidence = Math.Round(best.Score, 3),
            State = status,
            Explanation = Explain(winner, best.Member, best.NameScore, best.AgeScore, status)
        };
    }

    private static ScoredMember? Score(WinnerRecord winner, NameParts inscription, MemberRecord member)
    {
        var candidate = NameParts.From(member.FullName, member.FirstName, member.Initial, member.Surname);
        var surnameScore = JaroWinkler(inscription.Surname, candidate.Surname);
        if (surnameScore < 0.72) return null;

        var givenScore = GivenScore(inscription, candidate);
        var fullScore = JaroWinkler(inscription.Full, candidate.Full);
        var nameScore = Math.Clamp(surnameScore * 0.55 + givenScore * 0.3 + fullScore * 0.15, 0, 1);
        if (inscription.Full == candidate.Full) nameScore = 1;

        double? ageScore = null;
        if (member.BirthYear.HasValue)
        {
            var ageAtWin = winner.Year - member.BirthYear.Value;
            if (ageAtWin < 8 || ageAtWin > 100) return null;
            ageScore = ageAtWin is >= 12 and <= 85 ? 1 : 0.55;
        }

        var score = ageScore.HasValue ? nameScore * 0.84 + ageScore.Value * 0.16 : nameScore * 0.94;
        return new ScoredMember(member, Math.Clamp(score, 0, 1), nameScore, ageScore);
    }

    private static double GivenScore(NameParts inscription, NameParts candidate)
    {
        if (inscription.Given.Length == 0) return 0.55;
        if (inscription.Given.Length == 1)
            return candidate.Given.StartsWith(inscription.Given, StringComparison.Ordinal) ? 1 : 0;
        if (candidate.Given.Length == 1)
            return inscription.Given.StartsWith(candidate.Given, StringComparison.Ordinal) ? 0.92 : 0;
        return JaroWinkler(inscription.Given, candidate.Given);
    }

    private static string Explain(WinnerRecord winner, MemberRecord member, double nameScore, double? ageScore, string state)
    {
        var parts = new List<string>
        {
            state == MemberMatchStates.Strong ? "Strong name agreement" : "Possible name agreement"
        };
        if (member.BirthYear.HasValue)
            parts.Add($"age {winner.Year - member.BirthYear.Value} in {winner.Year} is plausible");
        else
            parts.Add("no birth year was available to test the date");
        parts.Add($"name score {Math.Round(nameScore * 100)}%{(ageScore.HasValue ? $", age check {Math.Round(ageScore.Value * 100)}%" : string.Empty)}");
        return string.Join("; ", parts) + ".";
    }

    private static double JaroWinkler(string left, string right)
    {
        if (left == right) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var range = Math.Max(left.Length, right.Length) / 2 - 1;
        var leftMatches = new bool[left.Length];
        var rightMatches = new bool[right.Length];
        var matches = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var start = Math.Max(0, i - range);
            var end = Math.Min(i + range + 1, right.Length);
            for (var j = start; j < end; j++)
            {
                if (rightMatches[j] || left[i] != right[j]) continue;
                leftMatches[i] = true;
                rightMatches[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0;
        var transpositions = 0;
        for (int i = 0, j = 0; i < left.Length; i++)
        {
            if (!leftMatches[i]) continue;
            while (!rightMatches[j]) j++;
            if (left[i] != right[j]) transpositions++;
            j++;
        }
        var m = (double)matches;
        var jaro = (m / left.Length + m / right.Length + (m - transpositions / 2d) / m) / 3d;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(left.Length, right.Length)) && left[prefix] == right[prefix]) prefix++;
        return jaro + prefix * 0.1 * (1 - jaro);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToUpperInvariant(character));
            else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ScoredMember(MemberRecord Member, double Score, double NameScore, double? AgeScore);

    private sealed record NameParts(string Full, string Given, string Surname)
    {
        public static NameParts From(string fullName, string? firstName = null, string? initial = null, string? surname = null)
        {
            var full = Normalize(fullName);
            var tokens = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolvedSurname = Normalize(surname ?? string.Empty);
            if (resolvedSurname.Length == 0) resolvedSurname = tokens.LastOrDefault() ?? string.Empty;
            var resolvedGiven = Normalize(firstName ?? string.Empty);
            if (resolvedGiven.Length == 0) resolvedGiven = Normalize(initial ?? string.Empty);
            if (resolvedGiven.Length == 0 && tokens.Length > 1) resolvedGiven = tokens[0];
            return new NameParts(full, resolvedGiven, resolvedSurname);
        }
    }
}
