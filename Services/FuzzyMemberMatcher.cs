using System.Globalization;
using System.Text;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class FuzzyMemberMatcher
{
    public MemberMatchRecord? FindBest(TrophyRecord trophy, WinnerRecord winner, IReadOnlyList<MemberRecord> members)
    {
        var candidates = ScoreCandidates(trophy, winner, members, 3);
        var best = candidates.FirstOrDefault();
        if (best is null || best.Score < 0.68) return null;

        var margin = candidates.Count > 1 ? best.Score - candidates[1].Score : best.Score;
        var state = best.Score >= 0.9 && margin >= 0.08 ? MemberMatchStates.Strong : MemberMatchStates.Possible;
        return ToMatch(trophy, winner, best, state, manuallySelected: false);
    }

    public IReadOnlyList<MemberMatchRecord> FindCandidates(
        TrophyRecord trophy,
        WinnerRecord winner,
        IReadOnlyList<MemberRecord> members,
        int limit = 20) =>
        ScoreCandidates(trophy, winner, members, Math.Clamp(limit, 1, 50))
            .Where(candidate => candidate.Score >= 0.45)
            .Select(candidate => ToMatch(
                trophy,
                winner,
                candidate,
                candidate.Score >= 0.9 ? MemberMatchStates.Strong : MemberMatchStates.Possible,
                manuallySelected: false))
            .ToList();

    public MemberMatchRecord? CreateSelection(
        TrophyRecord trophy,
        WinnerRecord winner,
        MemberRecord member)
    {
        var inscription = NameParts.From(winner.Name);
        var scored = inscription.Surname.Length == 0 ? null : Score(trophy, winner, inscription, member, enforcePlausibility: false);
        if (scored is null) return null;
        return ToMatch(trophy, winner, scored, MemberMatchStates.Strong, manuallySelected: true);
    }

    private static List<ScoredMember> ScoreCandidates(
        TrophyRecord trophy,
        WinnerRecord winner,
        IReadOnlyList<MemberRecord> members,
        int limit)
    {
        var inscription = NameParts.From(winner.Name);
        if (inscription.Surname.Length == 0) return [];

        return members
            .Where(member => !winner.RejectedMemberIds.Contains(member.Id, StringComparer.OrdinalIgnoreCase))
            .Select(member => Score(trophy, winner, inscription, member))
            .Where(candidate => candidate is not null)
            .Cast<ScoredMember>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Member.FullName)
            .Take(limit)
            .ToList();
    }

    private static ScoredMember? Score(
        TrophyRecord trophy,
        WinnerRecord winner,
        NameParts inscription,
        MemberRecord member,
        bool enforcePlausibility = true)
    {
        var candidate = NameParts.From(member.FullName, member.FirstName, member.Initial, member.Surname);
        var surnameAgreement = CompareSurnames(inscription.Surname, candidate.Surname);
        if (enforcePlausibility && !surnameAgreement.IsPlausible) return null;
        var surnameScore = surnameAgreement.Score;

        var givenScore = GivenScore(inscription, candidate);
        var fullScore = JaroWinkler(inscription.Full, candidate.Full);
        var nameScore = Math.Clamp(surnameScore * 0.55 + givenScore * 0.3 + fullScore * 0.15, 0, 1);
        if (inscription.Full == candidate.Full) nameScore = 1;

        int? ageAtAward = null;
        double? ageScore = null;
        if (member.BirthYear.HasValue)
        {
            ageAtAward = winner.Year - member.BirthYear.Value;
            if (ageAtAward < 5 || ageAtAward > 110)
            {
                if (enforcePlausibility) return null;
                ageScore = 0;
            }
            else ageScore = ageAtAward is >= 8 and <= 100 ? 1 : 0.45;
        }

        var score = ageScore.HasValue ? nameScore * 0.88 + ageScore.Value * 0.12 : nameScore * 0.95;
        var division = TrophyDivisions.Normalize(trophy.Division);
        double? divisionScore = null;

        if (division is TrophyDivisions.Gents or TrophyDivisions.Ladies)
        {
            var expectedGender = division == TrophyDivisions.Gents ? MemberGenders.Male : MemberGenders.Female;
            var gender = MemberGenders.Normalize(member.Gender);
            divisionScore = gender == MemberGenders.Unknown ? 0.55 : gender == expectedGender ? 1 : 0;
            score = score * 0.88 + divisionScore.Value * 0.12;
        }
        else if (division == TrophyDivisions.Junior)
        {
            divisionScore = ageAtAward.HasValue ? ageAtAward.Value <= 18 ? 1 : 0.08 : 0.55;
            score = score * 0.76 + divisionScore.Value * 0.24;
        }

        double? membershipTimelineScore = null;
        if (member.JoinYear.HasValue)
        {
            membershipTimelineScore = member.JoinYear.Value <= winner.Year ? 1 : 0.05;
            score = score * 0.82 + membershipTimelineScore.Value * 0.18;
        }

        return new ScoredMember(member, Math.Clamp(score, 0, 1), nameScore, ageScore, divisionScore, ageAtAward);
    }

    private static MemberMatchRecord ToMatch(
        TrophyRecord trophy,
        WinnerRecord winner,
        ScoredMember candidate,
        string state,
        bool manuallySelected) => new()
        {
            MemberId = candidate.Member.Id,
            MemberName = candidate.Member.FullName,
            MembershipNumber = candidate.Member.MembershipNumber,
            BirthYear = candidate.Member.BirthYear,
            JoinYear = candidate.Member.JoinYear,
            Gender = MemberGenders.Normalize(candidate.Member.Gender),
            Confidence = Math.Round(candidate.Score, 3),
            State = state,
            Explanation = Explain(trophy, winner, candidate, state, manuallySelected),
            ManuallySelected = manuallySelected
        };

    private static double GivenScore(NameParts inscription, NameParts candidate)
    {
        if (inscription.Given.Length == 0) return 0.55;
        if (inscription.Given.Length == 1)
            return candidate.Given.StartsWith(inscription.Given, StringComparison.Ordinal) ? 1 : 0;
        if (candidate.Given.Length == 1)
            return inscription.Given.StartsWith(candidate.Given, StringComparison.Ordinal) ? 0.92 : 0;
        return JaroWinkler(inscription.Given, candidate.Given);
    }

    private static string Explain(
        TrophyRecord trophy,
        WinnerRecord winner,
        ScoredMember candidate,
        string state,
        bool manuallySelected)
    {
        var member = candidate.Member;
        var parts = new List<string>
        {
            manuallySelected ? "Selected by the archive user" : state == MemberMatchStates.Strong ? "Strong name agreement" : "Possible name agreement"
        };
        if (candidate.AgeAtAward.HasValue)
            parts.Add(candidate.AgeAtAward.Value is < 5 or > 110
                ? $"birth year conflicts with the {winner.Year} award"
                : $"age {candidate.AgeAtAward.Value} in {winner.Year}");
        else
            parts.Add("no birth year was available to test the date");

        var division = TrophyDivisions.Normalize(trophy.Division);
        var gender = MemberGenders.Normalize(member.Gender);
        if (division is TrophyDivisions.Gents or TrophyDivisions.Ladies)
        {
            var expected = division == TrophyDivisions.Gents ? MemberGenders.Male : MemberGenders.Female;
            parts.Add(gender == MemberGenders.Unknown
                ? $"no gender was available for this {division} trophy"
                : gender == expected
                    ? $"gender agrees with this {division} trophy"
                    : $"gender differs from this {division} trophy");
        }
        else if (division == TrophyDivisions.Junior)
        {
            parts.Add(candidate.AgeAtAward.HasValue
                ? candidate.AgeAtAward.Value <= 18 ? "junior age preference met" : "older than 18 at the time"
                : "no birth year was available for the junior-age check");
        }

        if (member.JoinYear.HasValue)
        {
            parts.Add(member.JoinYear.Value <= winner.Year
                ? $"joined by the award year (recorded {member.JoinYear.Value})"
                : $"recorded joining year {member.JoinYear.Value} is after the {winner.Year} award");
        }

        parts.Add($"name score {Math.Round(candidate.NameScore * 100)}%");
        return string.Join("; ", parts) + ".";
    }

    private static SurnameAgreement CompareSurnames(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return new SurnameAgreement(0, false);
        if (left == right) return new SurnameAgreement(1, true);

        var distance = LevenshteinDistance(left, right);
        var longest = Math.Max(left.Length, right.Length);
        var editScore = Math.Clamp(1 - distance / (double)longest, 0, 1);
        var jaroScore = JaroWinkler(left, right);
        var score = jaroScore * 0.6 + editScore * 0.4;
        var plausible = distance <= 1
            || (longest >= 7 && distance <= 2)
            || (jaroScore >= 0.9 && editScore >= 0.6);
        return new SurnameAgreement(score, plausible);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1]
                    + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1);
                current[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static double JaroWinkler(string left, string right)
    {
        if (left == right) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var range = Math.Max(0, Math.Max(left.Length, right.Length) / 2 - 1);
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

    private sealed record ScoredMember(
        MemberRecord Member,
        double Score,
        double NameScore,
        double? AgeScore,
        double? DivisionScore,
        int? AgeAtAward);

    private sealed record SurnameAgreement(double Score, bool IsPlausible);

    private sealed record NameParts(string Full, string Given, string Surname)
    {
        public static NameParts From(string fullName, string? firstName = null, string? initial = null, string? surname = null)
        {
            var full = Normalize(fullName);
            var tokens = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolvedSurname = Normalize(surname ?? string.Empty);
            if (resolvedSurname.Length == 0)
            {
                // Keep punctuation-delimited surnames together until after the surname
                // has been selected. Normalising first turns "Bambrick-Sattar" into
                // separate tokens and would incorrectly compare only "Sattar".
                var originalTokens = fullName.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                resolvedSurname = Normalize(originalTokens.LastOrDefault() ?? string.Empty);
            }
            if (resolvedSurname.Length == 0) resolvedSurname = tokens.LastOrDefault() ?? string.Empty;
            var resolvedGiven = Normalize(firstName ?? string.Empty);
            if (resolvedGiven.Length == 0) resolvedGiven = Normalize(initial ?? string.Empty);
            if (resolvedGiven.Length == 0 && tokens.Length > 1) resolvedGiven = tokens[0];
            return new NameParts(full, resolvedGiven, resolvedSurname);
        }
    }
}
