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
        var matches = trophy.Winners.ToDictionary(
            winner => winner.Id,
            winner => members.Count == 0 ? null : matcher.FindBest(winner, members));
        return await catalogue.ApplyMemberMatchesAsync(trophyId, matches, cancellationToken);
    }

    public async Task<int> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await catalogue.GetSummariesAsync(cancellationToken);
        foreach (var summary in summaries) await RefreshTrophyAsync(summary.Id, cancellationToken);
        return summaries.Count;
    }
}
