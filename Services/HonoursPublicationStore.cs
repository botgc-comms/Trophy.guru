using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

/// <summary>Publication is an explicit, immutable copy, separate from transcription and member matching.</summary>
public sealed class HonoursPublicationStore(IWebHostEnvironment environment, IConfiguration configuration,
    CatalogueStore catalogue, AccountStore accounts, ClubContextAccessor clubContext, BillingStore? billing = null)
{
    private const long MaximumArtworkBytes = 64L * 1024 * 1024;
    private readonly string dataRoot = AppDataPath.Resolve(environment, configuration);
    private readonly ArchiveResourceLimits resourceLimits = new(configuration);
    private readonly SemaphoreSlim draftGate = new(1, 1);
    // Public callers can request arbitrary, nonexistent club IDs. A fixed set of stripes
    // bounds retained locks while keeping every operation on one club serialised.
    private readonly SemaphoreSlim[] publicationGates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static bool ValidClubId(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<HonoursPublication> GetAsync(string clubId, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        var gate = PublicationGate(clubId);
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
        var gate = PublicationGate(clubId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Catalogue access precedes the shared disk gate: catalogue writers take their
            // tenant lock before that gate, so reversing this order could deadlock.
            var draft = await BuildDraftAsync(clubId, input.Options, cancellationToken);
            if (!string.Equals(draft.Fingerprint, input.PreviewFingerprint, StringComparison.Ordinal))
                throw new PublicationException("preview_changed", "The records or display settings have changed. Review a fresh preview before publishing.");
            if (draft.Snapshot.Summary.Honours == 0)
                throw new PublicationException("no_winners_selected", "Choose at least one confirmed winner to publish.");
            await ArchiveResourceLimits.StorageGate.WaitAsync(cancellationToken);
            string? createdRevision = null;
            var committed = false;
            try
            {
                var current = await ReadUnsafeAsync(clubId, cancellationToken);
                if (current.IsPublic && await MatchesCurrentAsync(clubId, current, draft, cancellationToken))
                    return current; // A repeated approval cannot create another set of image copies.
                var previousRevision = current.Revision;
                var revision = Guid.NewGuid().ToString("N");
                var assetRoot = RevisionPath(clubId, revision);
                if (assetRoot is null || Directory.Exists(assetRoot))
                    throw new PublicationException("publication_storage", "The publication storage could not be prepared. Please try again.");
                var allowance = resourceLimits.Allowance(billing?.Balance(clubId));
                resourceLimits.CheckWrite(dataRoot, AppDataPath.ClubRoot(dataRoot, clubId), allowance,
                    draft.Assets.Values.Sum(asset => asset.Length) + Encoding.UTF8.GetByteCount(RevisionMarker(clubId, revision)));
                Directory.CreateDirectory(assetRoot);
                createdRevision = revision;
                await File.WriteAllTextAsync(Path.Combine(assetRoot, ".publication-revision"), RevisionMarker(clubId, revision), cancellationToken);
                var assets = new Dictionary<string, PublishedAsset>(StringComparer.Ordinal);
                foreach (var pair in draft.Assets)
                {
                    // Stream the exact previewed bytes; retain only paths/digests in memory.
                    var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Key))).ToLowerInvariant() + pair.Value.Extension;
                    var copyPath = Path.Combine(assetRoot, fileName);
                    await using (var source = new FileStream(pair.Value.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    await using (var destination = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await ArchiveResourceLimits.CopyBoundedAsync(source, destination, pair.Value.Length, cancellationToken);
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
                committed = true;
                RemoveSupersededRevisions(clubId, revision, previousRevision);
                return current;
            }
            finally
            {
                // Only this attempt's new copy may be removed after failure. Never delete
                // the previous good revision or an unknown directory to recover space.
                try { if (!committed && createdRevision is not null) TryDeleteRevision(clubId, createdRevision, createdByThisAttempt: true); }
                finally { ArchiveResourceLimits.StorageGate.Release(); }
            }
        }
        finally { gate.Release(); }
    }

    public async Task<HonoursPublication> WithdrawAsync(string clubId, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        var gate = PublicationGate(clubId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ArchiveResourceLimits.StorageGate.WaitAsync(cancellationToken);
            try
            {
                var state = await ReadUnsafeAsync(clubId, cancellationToken);
                state.IsPublic = false;
                state.WithdrawnAt = DateTimeOffset.UtcNow;
                state.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "withdrawn", state.Snapshot?.Summary.Honours ?? 0));
                state.Audit = state.Audit.TakeLast(100).ToList();
                await SaveUnsafeAsync(clubId, state, cancellationToken, enforceStorageQuota: false);
                return state;
            }
            finally { ArchiveResourceLimits.StorageGate.Release(); }
        }
        finally { gate.Release(); }
    }

    public async Task<(string Path, string ContentType)?> GetPublicAssetAsync(string clubId, string assetKey, CancellationToken cancellationToken = default)
    {
        ValidateClubId(clubId);
        var gate = PublicationGate(clubId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var publication = await ReadUnsafeAsync(clubId, cancellationToken);
            if (!publication.IsPublic || publication.Snapshot is null || publication.Revision is null ||
                !publication.Assets.TryGetValue(assetKey, out var asset)) return null;
            var path = Path.Combine(Root(clubId), Path.GetFileName(publication.Revision), Path.GetFileName(asset.FileName));
            return File.Exists(path) ? (path, asset.ContentType) : null;
        }
        finally { gate.Release(); }
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
        if (!await draftGate.WaitAsync(0, cancellationToken))
            throw new PublicationException("publication_busy", "Another publication preview is being prepared. Please try again shortly.");
        try { return await BuildDraftCoreAsync(clubId, input, cancellationToken); }
        finally { draftGate.Release(); }
    }

    private async Task<Draft> BuildDraftCoreAsync(string clubId, HonoursPublicationOptions input, CancellationToken cancellationToken)
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
        long artworkBytes = 0;
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
                var asset = await ReadDraftAssetAsync(imagePath, "image/png", ".png", MaximumArtworkBytes - artworkBytes, cancellationToken);
                artworkBytes += asset.Length;
                assets[$"trophy:{trophy.Id}"] = asset;
                imageUrl = $"/api/public/clubs/{Uri.EscapeDataString(clubId)}/trophies/{Uri.EscapeDataString(trophy.Id)}/illustration";
            }
            trophies.Add(new(trophy.Id, trophy.Name, trophy.SecondaryName, trophy.Category,
                TrophyDivisions.Normalize(trophy.Division), imageUrl, winners));
        }
        var logo = await accounts.GetLogoForClubAsync(clubId, cancellationToken);
        if (logo is not null)
            assets["logo"] = await ReadDraftAssetAsync(logo.Value.Path, logo.Value.ContentType,
                Path.GetExtension(logo.Value.Path), MaximumArtworkBytes - artworkBytes, cancellationToken);
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

    // Caller holds its publication stripe and the shared storage gate.
    private async Task SaveUnsafeAsync(string clubId, HonoursPublication state, CancellationToken cancellationToken, bool enforceStorageQuota = true)
    {
        Directory.CreateDirectory(Root(clubId));
        var path = Path.Combine(Root(clubId), "publication.json");
        var previousLength = File.Exists(path) ? new FileInfo(path).Length : 0;
        // Existing publications remain withdrawable even if they predate today's state
        // limit. The small margin accommodates a bounded audit entry, not new artwork.
        using var buffer = new BoundedArchiveStream(Math.Max(resourceLimits.StateBytes, previousLength + 32768));
        await JsonSerializer.SerializeAsync(buffer, state, JsonOptions, cancellationToken);
        if (enforceStorageQuota)
        {
            var allowance = resourceLimits.Allowance(billing?.Balance(clubId));
            resourceLimits.CheckWrite(dataRoot, AppDataPath.ClubRoot(dataRoot, clubId), allowance, buffer.Length, previousLength);
        }
        // Withdrawal adds no artwork and its audit is bounded. It must remain possible
        // when growth is paused at a quota or reserved disk-headroom threshold.
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<bool> MatchesCurrentAsync(string clubId, HonoursPublication current, Draft draft, CancellationToken cancellationToken)
    {
        if (current.Snapshot is null || current.Revision is null || current.Assets.Count != draft.Assets.Count ||
            JsonSerializer.Serialize(current.Snapshot, JsonOptions) != JsonSerializer.Serialize(draft.Snapshot, JsonOptions) ||
            JsonSerializer.Serialize(current.Options, JsonOptions) != JsonSerializer.Serialize(draft.Options, JsonOptions)) return false;
        var root = RevisionPath(clubId, current.Revision);
        if (root is null) return false;
        foreach (var pair in draft.Assets)
        {
            if (!current.Assets.TryGetValue(pair.Key, out var asset) || asset.ContentType != pair.Value.ContentType ||
                Path.GetFileName(asset.FileName) != asset.FileName) return false;
            var path = Path.Combine(root, asset.FileName);
            if (!File.Exists(path) || new FileInfo(path).Length != pair.Value.Length) return false;
            await using var stream = File.OpenRead(path);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (digest != pair.Value.Digest) return false;
        }
        return true;
    }

    private string? RevisionPath(string clubId, string revision)
    {
        if (revision.Length != 32 || revision.Any(character => !char.IsAsciiHexDigit(character))) return null;
        var root = Path.GetFullPath(Root(clubId));
        var path = Path.GetFullPath(Path.Combine(root, revision));
        return string.Equals(Path.GetDirectoryName(path), root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? path : null;
    }

    private void RemoveSupersededRevisions(string clubId, string current, string? previous)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(Root(clubId)))
            {
                var revision = Path.GetFileName(directory);
                if (revision.Equals(current, StringComparison.OrdinalIgnoreCase) || revision.Equals(previous, StringComparison.OrdinalIgnoreCase)) continue;
                TryDeleteRevision(clubId, revision);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string RevisionMarker(string clubId, string revision) => $"TrophyArchive publication v1\n{clubId}\n{revision}\n";

    private void TryDeleteRevision(string clubId, string revision, bool createdByThisAttempt = false)
    {
        try
        {
            var path = RevisionPath(clubId, revision);
            if (path is null || !Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            // Only revisions created with this ownership marker may be pruned. Older or
            // unexpected folders are retained for review, never guessed to be disposable.
            var marker = Path.Combine(path, ".publication-revision");
            if (!createdByThisAttempt && (!File.Exists(marker) || new FileInfo(marker).Length > 200 ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 || File.ReadAllText(marker) != RevisionMarker(clubId, revision))) return;
            if (Directory.EnumerateDirectories(path).Any()) return;
            var files = Directory.GetFiles(path);
            if (files.Any(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                (file != marker && (Path.GetFileNameWithoutExtension(file).Length != 64 ||
                    Path.GetFileNameWithoutExtension(file).Any(character => !char.IsAsciiHexDigit(character)) ||
                    Path.GetExtension(file).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))))) return;
            foreach (var file in files.Where(file => file != marker)) File.Delete(file);
            if (File.Exists(marker)) File.Delete(marker);
            Directory.Delete(path, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private SemaphoreSlim PublicationGate(string clubId)
    {
        // Stable FNV-1a; ASCII IDs differing only by case share a stripe, including the
        // legacy alias and paths on a case-insensitive filesystem. Collisions only queue
        // unrelated clubs briefly; no caller-controlled keys are retained.
        var hash = 2166136261u;
        foreach (var character in clubId)
            hash = unchecked((hash ^ char.ToUpperInvariant(character)) * 16777619u);
        return publicationGates[hash % (uint)publicationGates.Length];
    }

    private string Root(string clubId) => Path.Combine(AppDataPath.ClubRoot(dataRoot, clubId), "honours-publication");
    private static void ValidateClubId(string clubId)
    {
        if (!ValidClubId(clubId)) throw new PublicationException("invalid_club", "Invalid club identifier.");
    }
    private static async Task<DraftAsset> ReadDraftAssetAsync(string path, string contentType, string extension, long remainingBytes, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > remainingBytes)
            throw new PublicationException("publication_artwork_limit", "The selected artwork must contain data and total no more than 64 MiB. Select fewer trophies before publishing.");
        await using var stream = File.OpenRead(path);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (stream.Length != length) throw new PublicationException("preview_changed", "An image changed while the preview was being prepared. Please try again.");
        return new(path, contentType, extension, digest, length);
    }
    private sealed record DraftAsset(string Path, string ContentType, string Extension, string Digest, long Length);
    private sealed record Draft(PublishedHonours Snapshot, HonoursPublicationOptions Options, string Fingerprint, Dictionary<string, DraftAsset> Assets);
}

public sealed class PublicationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
