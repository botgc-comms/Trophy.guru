using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

/// <summary>Publication is an explicit, immutable copy, separate from transcription and member matching.</summary>
public sealed class HonoursPublicationStore(IWebHostEnvironment environment, IConfiguration configuration,
    CatalogueStore catalogue, AccountStore accounts, ClubContextAccessor clubContext)
{
    private readonly string dataRoot = AppDataPath.Resolve(environment, configuration);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static bool ValidClubId(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<HonoursPublication> GetAsync(string clubId, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        var gate = gates.GetOrAdd(clubId, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await ReadUnsafeAsync(clubId, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PublicationCandidate>> GetCandidatesAsync(string clubId, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        using var scope = clubContext.Push(clubId);
        var trophies = await catalogue.GetTrophiesAsync(cancellationToken);
        return trophies.OrderBy(t => t.Name, StringComparer.Ordinal).SelectMany(t =>
            t.Winners.Where(w => w.ReviewState == ReviewStates.Confirmed).OrderByDescending(w => w.Year)
                .ThenBy(w => w.Id, StringComparer.Ordinal).Select(w => new PublicationCandidate(WinnerKey(t.Id, w.Id),
                    t.Name, TrophyDivisions.Normalize(t.Division), w.Year, PublicHonoursNameFormatter.Format(w.Name),
                    ApprovedIdentity(w) is { } match ? PublicHonoursNameFormatter.Format(match.MemberName) : null, w.Description))).ToList();
    }

    public async Task<PublicationPreview> PreviewAsync(string clubId, HonoursPublicationOptions options, CancellationToken cancellationToken = default)
    {
        var draft = await BuildDraftAsync(clubId, options, cancellationToken);
        return new(draft.Snapshot, draft.Fingerprint, draft.Options);
    }

    public async Task<HonoursPublication> PublishAsync(string clubId, string actorId, PublishHonoursInput input, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        if (!input.PublicationApproved) throw new PublicationException("publication_approval_required", "Approve the public publication decision before publishing.");
        var gate = gates.GetOrAdd(clubId, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var draft = await BuildDraftAsync(clubId, input.Options, cancellationToken);
            if (!string.Equals(draft.Fingerprint, input.PreviewFingerprint, StringComparison.Ordinal))
                throw new PublicationException("preview_changed", "The records or display settings have changed. Review a fresh preview before publishing.");
            if (draft.Snapshot.Summary.Honours == 0)
                throw new PublicationException("no_winners_selected", "Choose at least one confirmed winner to publish.");
            var current = await ReadUnsafeAsync(clubId, cancellationToken);
            var revision = Guid.NewGuid().ToString("N");
            var assetRoot = Path.Combine(Root(clubId), revision);
            Directory.CreateDirectory(assetRoot);
            var assets = new Dictionary<string, PublishedAsset>(StringComparer.Ordinal);
            foreach (var pair in draft.Assets)
            {
                // Save exactly the bytes the owner previewed; generation or uploads after preview cannot silently replace them.
                var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Key))).ToLowerInvariant() + pair.Value.Extension;
                var copyPath = Path.Combine(assetRoot, fileName);
                await using (var source = new FileStream(pair.Value.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await using (var destination = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await source.CopyToAsync(destination, cancellationToken);
                await using (var copy = File.OpenRead(copyPath))
                {
                    var copiedDigest = Convert.ToHexString(await SHA256.HashDataAsync(copy, cancellationToken));
                    if (!string.Equals(copiedDigest, pair.Value.Digest, StringComparison.Ordinal))
                        throw new PublicationException("preview_changed", "An image changed during publication. Review a fresh preview before publishing.");
                }
                assets[pair.Key] = new(fileName, pair.Value.ContentType);
            }
            current.IsPublic = true;
            current.Revision = revision;
            current.Snapshot = draft.Snapshot;
            current.Options = draft.Options;
            current.Assets = assets;
            current.PublishedAt = DateTimeOffset.UtcNow;
            current.WithdrawnAt = null;
            current.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "published", draft.Snapshot.Summary.Honours));
            current.Audit = current.Audit.TakeLast(100).ToList();
            await SaveUnsafeAsync(clubId, current, cancellationToken);
            return current;
        }
        finally { gate.Release(); }
    }

    public async Task<HonoursPublication> WithdrawAsync(string clubId, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        var gate = gates.GetOrAdd(clubId, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(clubId, cancellationToken);
            state.IsPublic = false;
            state.WithdrawnAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "withdrawn", state.Snapshot?.Summary.Honours ?? 0));
            state.Audit = state.Audit.TakeLast(100).ToList();
            await SaveUnsafeAsync(clubId, state, cancellationToken);
            return state;
        }
        finally { gate.Release(); }
    }

    public async Task<(string Path, string ContentType)?> GetPublicAssetAsync(string clubId, string assetKey, CancellationToken cancellationToken = default)
    {
        var publication = await GetAsync(clubId, cancellationToken);
        if (!publication.IsPublic || publication.Snapshot is null || publication.Revision is null ||
            !publication.Assets.TryGetValue(assetKey, out var asset)) return null;
        var path = Path.Combine(Root(clubId), Path.GetFileName(publication.Revision), Path.GetFileName(asset.FileName));
        return File.Exists(path) ? (path, asset.ContentType) : null;
    }

    public static string WinnerKey(string trophyId, string winnerId) => $"{trophyId}:{winnerId}";

    public static HonoursPublicationOptions NormalizeOptions(HonoursPublicationOptions? input)
    {
        if (input is null || input.NamePolicy is not ("inscription" or "approved-identities"))
            throw new PublicationException("invalid_name_policy", "Choose inscription names or manually approved identities.");
        if (input.SelectedWinnerKeys is null || input.SelectedWinnerKeys.Count > 100000)
            throw new PublicationException("invalid_selection", "Choose the confirmed winners to publish.");
        if (input.AllowedEmbedOrigins is null || input.AllowedEmbedOrigins.Count > 20)
            throw new PublicationException("invalid_embed_origins", "Add no more than 20 website origins.");
        var origins = input.AllowedEmbedOrigins.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value =>
        {
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) ||
                uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
                uri.Host.Contains('*'))
                throw new PublicationException("invalid_embed_origin", "Use a complete HTTPS website origin, for example https://www.yourclub.co.uk, without a page path or wildcard.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new()
        {
            NamePolicy = input.NamePolicy, IncludeDescriptions = input.IncludeDescriptions,
            IncludeJuniorTrophies = input.IncludeJuniorTrophies, AllowedEmbedOrigins = origins,
            SelectedWinnerKeys = input.SelectedWinnerKeys.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
        };
    }

    private async Task<Draft> BuildDraftAsync(string clubId, HonoursPublicationOptions input, CancellationToken cancellationToken)
    {
        ValidateClubId(clubId);
        var options = NormalizeOptions(input);
        var club = await accounts.GetClubAsync(clubId, cancellationToken)
            ?? throw new PublicationException("club_missing", "The club could not be found.");
        using var scope = clubContext.Push(clubId);
        var source = await catalogue.GetTrophiesAsync(cancellationToken);
        var selected = options.SelectedWinnerKeys.ToHashSet(StringComparer.Ordinal);
        var eligible = source.SelectMany(t => t.Winners.Where(w => w.ReviewState == ReviewStates.Confirmed &&
            (options.IncludeJuniorTrophies || TrophyDivisions.Normalize(t.Division) != TrophyDivisions.Junior))
            .Select(w => WinnerKey(t.Id, w.Id))).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(key => !eligible.Contains(key)))
            throw new PublicationException("selection_changed", "A selected record is no longer confirmed or is excluded by your junior trophy setting. Refresh the records and review your selection.");
        var assets = new Dictionary<string, DraftAsset>(StringComparer.Ordinal);
        var trophies = new List<PublishedTrophy>();
        foreach (var trophy in source.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            var winners = trophy.Winners.Where(w => selected.Contains(WinnerKey(trophy.Id, w.Id)))
                .OrderByDescending(w => w.Year).ThenBy(w => w.Id, StringComparer.Ordinal).Select(w =>
                {
                    var approved = options.NamePolicy == "approved-identities" ? ApprovedIdentity(w) : null;
                    var name = PublicHonoursNameFormatter.Format(approved?.MemberName ?? w.Name);
                    // Never expose member identifiers, birth/join years or matching metadata in the public payload.
                    var identity = approved is null ? $"name:{name.ToLowerInvariant()}" : $"member:{approved.MemberId}";
                    var personId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{clubId}\n{identity}"))).ToLowerInvariant()[..24];
                    return new PublishedWinner(w.Year, name, options.IncludeDescriptions ? w.Description : null, personId);
                }).ToList();
            if (winners.Count == 0) continue;
            var imagePath = await catalogue.GetIllustrationPathAsync(trophy.Id, cancellationToken);
            if (imagePath is null && trophy.ReferenceImage is { } reference && reference.StartsWith("/catalogue/", StringComparison.OrdinalIgnoreCase))
            {
                // Original catalogue illustrations are generic artwork; publish a frozen PNG copy through the same gate.
                var name = Path.GetFileNameWithoutExtension(reference);
                var candidate = Path.Combine(environment.WebRootPath, "catalogue", $"{name}.png");
                if (File.Exists(candidate)) imagePath = candidate;
            }
            string? imageUrl = null;
            if (imagePath is not null)
            {
                assets[$"trophy:{trophy.Id}"] = await ReadDraftAssetAsync(imagePath, "image/png", ".png", cancellationToken);
                imageUrl = $"/api/public/clubs/{Uri.EscapeDataString(clubId)}/trophies/{Uri.EscapeDataString(trophy.Id)}/illustration";
            }
            trophies.Add(new(trophy.Id, trophy.Name, trophy.SecondaryName, trophy.Category,
                TrophyDivisions.Normalize(trophy.Division), imageUrl, winners));
        }
        var logo = await accounts.GetLogoForClubAsync(clubId, cancellationToken);
        if (logo is not null)
            assets["logo"] = await ReadDraftAssetAsync(logo.Value.Path, logo.Value.ContentType,
                Path.GetExtension(logo.Value.Path), cancellationToken);
        var allWinners = trophies.SelectMany(t => t.Winners).ToList();
        var years = allWinners.Select(w => w.Year).Distinct().Order().ToList();
        var snapshot = new PublishedHonours(new(club.Id, club.Name, club.Sport, club.Country, club.Website,
            logo is null ? null : $"/api/public/clubs/{Uri.EscapeDataString(clubId)}/logo"),
            new(trophies.Count, allWinners.Count, allWinners.Select(w => w.PersonId).Distinct().Count(), years.Count,
                years.Count == 0 ? null : years[0], years.Count == 0 ? null : years[^1]), trophies);
        var fingerprintMaterial = JsonSerializer.Serialize(new { snapshot, options,
            assets = assets.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key,
                hash = pair.Value.Digest, pair.Value.ContentType }).ToList() }, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial))).ToLowerInvariant();
        return new(snapshot, options, fingerprint, assets);
    }

    private static MemberMatchRecord? ApprovedIdentity(WinnerRecord winner) =>
        !winner.KeepMemberUnmatched && winner.MemberMatch is { ManuallySelected: true } match &&
        !string.IsNullOrWhiteSpace(match.MemberName) && !string.IsNullOrWhiteSpace(match.MemberId) ? match : null;

    private async Task<HonoursPublication> ReadUnsafeAsync(string clubId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Root(clubId), "publication.json");
        if (!File.Exists(path)) return new(); // No migration or implied consent from existing confirmed records.
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<HonoursPublication>(stream, JsonOptions, cancellationToken) ?? new();
    }

    private async Task SaveUnsafeAsync(string clubId, HonoursPublication state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root(clubId));
        var path = Path.Combine(Root(clubId), "publication.json");
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private string Root(string clubId) => Path.Combine(AppDataPath.ClubRoot(dataRoot, clubId), "honours-publication");
    private static void ValidateClubId(string clubId)
    {
        if (!ValidClubId(clubId)) throw new PublicationException("invalid_club", "Invalid club identifier.");
    }
    private static async Task<DraftAsset> ReadDraftAssetAsync(string path, string contentType, string extension, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return new(path, contentType, extension, digest);
    }
    private sealed record DraftAsset(string Path, string ContentType, string Extension, string Digest);
    private sealed record Draft(PublishedHonours Snapshot, HonoursPublicationOptions Options, string Fingerprint, Dictionary<string, DraftAsset> Assets);
}

public sealed class PublicationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
