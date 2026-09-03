using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class MemberMatchingCoordinator(
    CatalogueStore catalogue,
    MemberDirectoryStore directory,
    FuzzyMemberMatcher matcher)
{
    public async Task<TrophyRecord?> RefreshTrophyAsync(string trophyId, CancellationToken cancellationToken = default)
    {
        var trophy = await catalogue.GetTrophyAsync(trophyId, cancellationToken);
        if (trophy is null) return null;
        var members = await directory.GetMembersAsync(cancellationToken);
        var memberIds = members.Select(member => member.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = trophy.Winners.ToDictionary(
            winner => winner.Id,
            winner => winner.MemberMatch?.ManuallySelected == true && memberIds.Contains(winner.MemberMatch.MemberId)
                ? winner.MemberMatch
                : members.Count == 0 ? null : matcher.FindBest(trophy, winner, members));
        return await catalogue.ApplyMemberMatchesAsync(trophyId, matches, cancellationToken);
    }

    public async Task<IReadOnlyList<MemberMatchRecord>?> GetCandidatesAsync(
        string trophyId,
        string winnerId,
        CancellationToken cancellationToken = default)
    {
        var trophy = await catalogue.GetTrophyAsync(trophyId, cancellationToken);
        var winner = trophy?.Winners.FirstOrDefault(item => item.Id == winnerId);
        if (trophy is null || winner is null) return null;
        var members = await directory.GetMembersAsync(cancellationToken);
        return matcher.FindCandidates(trophy, winner, members);
    }

    public async Task<TrophyRecord?> SelectMemberAsync(
        string trophyId,
        string winnerId,
        string memberId,
        CancellationToken cancellationToken = default)
    {
        var trophy = await catalogue.GetTrophyAsync(trophyId, cancellationToken);
        var winner = trophy?.Winners.FirstOrDefault(item => item.Id == winnerId);
        if (trophy is null || winner is null) return null;
        var member = (await directory.GetMembersAsync(cancellationToken))
            .FirstOrDefault(item => item.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
        if (member is null) return null;
        var match = matcher.CreateSelection(trophy, winner, member);
        return match is null ? null : await catalogue.SetMemberMatchAsync(trophyId, winnerId, match, cancellationToken);
    }

    public async Task<TrophyRecord?> AddAndSelectMemberAsync(
        string trophyId,
        string winnerId,
        ManualMemberInput input,
        CancellationToken cancellationToken = default)
    {
        var trophy = await catalogue.GetTrophyAsync(trophyId, cancellationToken);
        var winner = trophy?.Winners.FirstOrDefault(item => item.Id == winnerId);
        if (trophy is null || winner is null) return null;

        var member = await directory.AddManualMemberAsync(input, cancellationToken);
        var match = matcher.CreateSelection(trophy, winner, member);
        return match is null ? null : await catalogue.SetMemberMatchAsync(trophyId, winnerId, match, cancellationToken);
    }

    public async Task<int> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await catalogue.GetSummariesAsync(cancellationToken);
        foreach (var summary in summaries) await RefreshTrophyAsync(summary.Id, cancellationToken);
        return summaries.Count;
    }
}
