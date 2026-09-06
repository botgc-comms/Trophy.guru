using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class PublicationLockSecurityTests
{
    [Fact]
    public async Task TenThousandUnknownClubsRemainPrivateWithoutRetainingMoreLocksOrCreatingFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        var locksBefore = Locks(fixture.Store);
        Assert.InRange(locksBefore.Length, 1, 256);
        var filesBefore = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var directoriesBefore = Directory.GetDirectories(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();

        await Parallel.ForEachAsync(Enumerable.Range(0, 10000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (index, cancellationToken) =>
        {
            var clubId = "unknown-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var publication = await fixture.Store.GetAsync(clubId, cancellationToken);
            Assert.False(publication.IsPublic);
            Assert.Null(publication.Snapshot);
            Assert.Null(await fixture.Store.GetPublicAssetAsync(clubId, "logo", cancellationToken));
        });

        var locksAfter = Locks(fixture.Store);
        Assert.Same(locksBefore, locksAfter);
        Assert.All(locksAfter, gate => Assert.Equal(1, gate.CurrentCount));
        Assert.Equal(filesBefore, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal(directoriesBefore, Directory.GetDirectories(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task ConcurrentPublishingWithdrawalAndReadsKeepOneConsistentAuditAndPreservePrivateArchive()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] };
        var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
        var sourceBefore = await File.ReadAllBytesAsync(fixture.CataloguePath);
        var identityBefore = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(0, 32).Select(async index =>
        {
            await start.Task;
            if (index % 2 == 0)
                await fixture.Store.PublishAsync(Fixture.ClubId, "publisher-" + index, new(options, preview.Fingerprint, true));
            else
                await fixture.Store.WithdrawAsync(Fixture.ClubId, "withdrawer-" + index);
        }).ToArray();
        var reads = Enumerable.Range(0, 8).Select(async _ =>
        {
            await start.Task;
            for (var index = 0; index < 12; index++)
            {
                var publication = await fixture.Store.GetAsync(Fixture.ClubId);
                if (publication.IsPublic)
                {
                    Assert.NotNull(publication.Revision);
                    Assert.NotNull(publication.Snapshot);
                    Assert.Equal(1, publication.Snapshot.Summary.Honours);
                    Assert.Equal("published", publication.Audit.Last().Action);
                }
                else if (publication.Audit.Count > 0)
                    Assert.Equal("withdrawn", publication.Audit.Last().Action);
                var asset = await fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "logo");
                if (asset is not null) Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(asset.Value.Path));
            }
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(writes.Concat(reads));

        var final = await fixture.Store.GetAsync(Fixture.ClubId);
        // Identical already-public approvals are idempotent; every withdrawal is audited.
        Assert.InRange(final.Audit.Count, 17, 32);
        Assert.Equal(16, final.Audit.Count(entry => entry.Action == "withdrawn"));
        Assert.Equal(final.Audit.Count, final.Audit.Select(entry => entry.ActorId).Distinct().Count());
        Assert.Equal(final.Audit.Last().Action == "published", final.IsPublic);
        await fixture.Store.WithdrawAsync(Fixture.ClubId, "final-withdrawal");
        Assert.False((await fixture.Store.GetAsync(Fixture.ClubId)).IsPublic);
        Assert.Null(await fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "logo"));
        Assert.Null(await fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "trophy:CUP"));
        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(fixture.CataloguePath));
        Assert.Equal(identityBefore, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json")));
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(fixture.LogoPath));
    }

    [Fact]
    public async Task AllPublicationOperationsUseTheSameCaseInsensitiveStripeAndCancellationCannotReleaseIt()
    {
        using var fixture = await Fixture.CreateAsync();
        var gate = Gate(fixture.Store, Fixture.ClubId);
        Assert.Same(gate, Gate(fixture.Store, Fixture.ClubId.ToUpperInvariant()));
        Assert.Same(Gate(fixture.Store, "legacy"), Gate(fixture.Store, "LEGACY"));
        var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] };
        var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
        await gate.WaitAsync();
        Task<HonoursPublication> read;
        Task<(string Path, string ContentType)?> asset;
        Task<HonoursPublication> publish;
        Task<HonoursPublication> withdraw;
        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.GetAsync(Fixture.ClubId, cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "logo", cancelled.Token));
            Assert.Equal(0, gate.CurrentCount);
            read = fixture.Store.GetAsync(Fixture.ClubId);
            asset = fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "logo");
            publish = fixture.Store.PublishAsync(Fixture.ClubId, "publisher", new(options, preview.Fingerprint, true));
            withdraw = fixture.Store.WithdrawAsync(Fixture.ClubId, "withdrawer");
            Assert.False(read.IsCompleted);
            Assert.False(asset.IsCompleted);
            Assert.False(publish.IsCompleted);
            Assert.False(withdraw.IsCompleted);
        }
        finally { gate.Release(); }
        await Task.WhenAll(read, asset, publish, withdraw);
        Assert.Equal(1, gate.CurrentCount);
        var final = await fixture.Store.GetAsync(Fixture.ClubId);
        Assert.Equal(2, final.Audit.Count);
        Assert.Equal(final.Audit.Last().Action == "published", final.IsPublic);
    }

    [Fact]
    public async Task RepeatedPublicationKeepsTheSameGoldenSnapshotWithoutMoreFilesOrAuditEntries()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] };
        var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
        var first = await fixture.Store.PublishAsync(Fixture.ClubId, "first-owner", new(options, preview.Fingerprint, true));
        var publicationPath = Path.Combine(fixture.PublicationRoot, "publication.json");
        var golden = await File.ReadAllBytesAsync(publicationPath);
        var files = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var bytes = ArchiveResourceLimits.DirectoryBytes(fixture.Root);
        for (var index = 0; index < 8; index++)
        {
            var same = await fixture.Store.PublishAsync(Fixture.ClubId, "repeat-owner", new(options, preview.Fingerprint, true));
            Assert.Equal(first.Revision, same.Revision);
            Assert.Equal(first.PublishedAt, same.PublishedAt);
            Assert.Single(same.Audit);
        }
        Assert.Equal(golden, await File.ReadAllBytesAsync(publicationPath));
        Assert.Equal(files, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal(bytes, ArchiveResourceLimits.DirectoryBytes(fixture.Root));
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(fixture.LogoPath));
    }

    [Fact]
    public async Task SuccessfulVersionsRetainCurrentAndPreviousCopiesAndLeaveUnknownFoldersAlone()
    {
        using var fixture = await Fixture.CreateAsync();
        var sourceBefore = await File.ReadAllBytesAsync(fixture.CataloguePath);
        Directory.CreateDirectory(fixture.PublicationRoot);
        var unknown = Path.Combine(fixture.PublicationRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unknown);
        var unknownAsset = Path.Combine(unknown, new string('a', 64) + ".png");
        await File.WriteAllBytesAsync(unknownAsset, [7, 8, 9]);
        var revisions = new List<string>();
        for (var index = 0; index < 4; index++)
        {
            var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"], AllowedEmbedOrigins = [$"https://version-{index}.example"] };
            var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
            var result = await fixture.Store.PublishAsync(Fixture.ClubId, "owner", new(options, preview.Fingerprint, true));
            revisions.Add(result.Revision!);
        }
        Assert.False(Directory.Exists(Path.Combine(fixture.PublicationRoot, revisions[0])));
        Assert.False(Directory.Exists(Path.Combine(fixture.PublicationRoot, revisions[1])));
        Assert.True(Directory.Exists(Path.Combine(fixture.PublicationRoot, revisions[2])));
        Assert.True(Directory.Exists(Path.Combine(fixture.PublicationRoot, revisions[3])));
        Assert.Equal(3, Directory.GetDirectories(fixture.PublicationRoot).Length); // Two owned copies plus the untouched unknown folder.
        Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(unknownAsset));
        var asset = await fixture.Store.GetPublicAssetAsync(Fixture.ClubId, "logo");
        Assert.NotNull(asset);
        Assert.Contains(revisions[3], asset.Value.Path);
        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(fixture.CataloguePath));
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(fixture.LogoPath));
    }

    [Fact]
    public async Task FailedPublicationCommitRetainsTheLastGoodSnapshotAssetsAndOriginals()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] };
        var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
        var first = await fixture.Store.PublishAsync(Fixture.ClubId, "owner", new(options, preview.Fingerprint, true));
        var golden = await File.ReadAllBytesAsync(Path.Combine(fixture.PublicationRoot, "publication.json"));
        var identity = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json"));
        var catalogue = await File.ReadAllBytesAsync(fixture.CataloguePath);
        var bytesBefore = ArchiveResourceLimits.DirectoryBytes(fixture.Root);
        // Enough for the eight fixture artwork bytes and a marker, but not a larger state.
        var tenantBytes = ArchiveResourceLimits.DirectoryBytes(Path.Combine(fixture.Root, "clubs", Fixture.ClubId));
        var limited = fixture.WithStorageLimit(tenantBytes + 128);
        var changed = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"], AllowedEmbedOrigins = ["https://" + new string('a', 60) + ".example"] };
        var replacement = await limited.PreviewAsync(Fixture.ClubId, changed);
        var failure = await Assert.ThrowsAsync<BillingException>(() => limited.PublishAsync(Fixture.ClubId, "owner", new(changed, replacement.Fingerprint, true)));
        Assert.Equal("storage_limit", failure.Code);
        Assert.Equal(golden, await File.ReadAllBytesAsync(Path.Combine(fixture.PublicationRoot, "publication.json")));
        Assert.Equal(first.Revision, (await limited.GetAsync(Fixture.ClubId)).Revision);
        Assert.Single(Directory.GetDirectories(fixture.PublicationRoot));
        var asset = await limited.GetPublicAssetAsync(Fixture.ClubId, "logo");
        Assert.NotNull(asset);
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(asset.Value.Path));
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(fixture.LogoPath));
        Assert.Equal(catalogue, await File.ReadAllBytesAsync(fixture.CataloguePath));
        Assert.Equal(identity, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json")));
        Assert.Equal(bytesBefore, ArchiveResourceLimits.DirectoryBytes(fixture.Root));
    }

    [Fact]
    public async Task AnOverQuotaClubCanWithdrawWithoutRemovingItsSnapshotOrOriginalFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] };
        var preview = await fixture.Store.PreviewAsync(Fixture.ClubId, options);
        await fixture.Store.PublishAsync(Fixture.ClubId, "owner", new(options, preview.Fingerprint, true));
        var catalogue = await File.ReadAllBytesAsync(fixture.CataloguePath);
        var limited = fixture.WithStorageLimit(1);
        var withdrawn = await limited.WithdrawAsync(Fixture.ClubId, "owner");
        Assert.False(withdrawn.IsPublic);
        Assert.NotNull(withdrawn.Snapshot);
        Assert.Equal(1, withdrawn.Snapshot.Summary.Honours);
        Assert.Null(await limited.GetPublicAssetAsync(Fixture.ClubId, "logo"));
        Assert.Equal(catalogue, await File.ReadAllBytesAsync(fixture.CataloguePath));
        Assert.Equal(Fixture.LogoBytes, await File.ReadAllBytesAsync(fixture.LogoPath));
    }

    [Fact]
    public async Task ExcessiveArtworkIsRejectedBeforeOpeningOrHashingTheOversizedFile()
    {
        using var fixture = await Fixture.CreateAsync();
        var catalogueBefore = await File.ReadAllBytesAsync(fixture.CataloguePath);
        // A sparse-sized fixture file avoids any large in-memory bitmap allocation. The
        // exclusive handle also proves the reader rejects its length before opening it.
        await using var locked = new FileStream(fixture.LogoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        locked.SetLength(64L * 1024 * 1024 + 1);
        var error = await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PreviewAsync(Fixture.ClubId,
            new HonoursPublicationOptions { SelectedWinnerKeys = ["CUP:winner"] }));
        Assert.Equal("publication_artwork_limit", error.Code);
        Assert.False(Directory.Exists(fixture.PublicationRoot));
        Assert.Equal(catalogueBefore, await File.ReadAllBytesAsync(fixture.CataloguePath));
    }

    // Inspect retained locks directly: allocation/GC measurements are noisy and would not
    // reliably catch the anonymous-ID dictionary that this regression protects against.
    private static SemaphoreSlim[] Locks(HonoursPublicationStore store) => Assert.IsType<SemaphoreSlim[]>(
        typeof(HonoursPublicationStore).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(field => field.FieldType == typeof(SemaphoreSlim[])).GetValue(store));
    private static SemaphoreSlim Gate(HonoursPublicationStore store, string clubId) => Assert.IsType<SemaphoreSlim>(
        typeof(HonoursPublicationStore).GetMethod("PublicationGate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(store, [clubId]));

    private sealed class Fixture : IDisposable
    {
        public const string ClubId = "fixture-club";
        public static readonly byte[] LogoBytes = [1, 2, 3, 4];
        public required string Root { get; init; }
        public required string CataloguePath { get; init; }
        public required string LogoPath { get; init; }
        public string PublicationRoot => Path.Combine(Root, "clubs", ClubId, "honours-publication");
        public required CatalogueStore Catalogue { get; init; }
        public required AccountStore Accounts { get; init; }
        public required ClubContextAccessor Context { get; init; }
        public required HonoursPublicationStore Store { get; init; }
        public HonoursPublicationStore WithStorageLimit(long bytes)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATA_PATH"] = Root, ["SKIP_SEED_CATALOGUE"] = "true", ["ARCHIVE_FREE_STORAGE_BYTES"] = bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Build();
            return new(new TestEnvironment(Root), configuration, Catalogue, Accounts, Context);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "trophy-publication-lock-tests-" + Guid.NewGuid().ToString("N"));
            var clubRoot = Path.Combine(root, "clubs", ClubId);
            Directory.CreateDirectory(Path.Combine(clubRoot, "brand"));
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "catalogue"));
            var logoPath = Path.Combine(clubRoot, "brand", "logo.png");
            await File.WriteAllBytesAsync(logoPath, LogoBytes);
            await File.WriteAllBytesAsync(Path.Combine(root, "wwwroot", "catalogue", "test.png"), [4, 3, 2, 1]);
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var identity = new IdentityState
            {
                Clubs = [new() { Id = ClubId, Name = "Fictional Lock Test Club", Sport = "Golf", Country = "United Kingdom", LogoStoredName = "logo.png", LogoContentType = "image/png" }]
            };
            await File.WriteAllTextAsync(Path.Combine(root, "identity.json"), JsonSerializer.Serialize(identity, json));
            var cataloguePath = Path.Combine(clubRoot, "catalogue-state.json");
            var state = new CatalogueState
            {
                Trophies = [new() { Id = "CUP", Name = "Fixture Cup", Category = "Golf", ReferenceImage = "/catalogue/test.png",
                    Winners = [new() { Id = "winner", Year = 2020, Name = "A. Fixture", ReviewState = ReviewStates.Confirmed }] }]
            };
            await File.WriteAllTextAsync(cataloguePath, JsonSerializer.Serialize(state, json));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATA_PATH"] = root, ["SKIP_SEED_CATALOGUE"] = "true"
            }).Build();
            var environment = new TestEnvironment(root);
            var context = new ClubContextAccessor();
            var accounts = new AccountStore(environment, configuration, new PasswordHasher<AccountRecord>());
            await accounts.InitializeAsync();
            var catalogue = new CatalogueStore(environment, configuration, context);
            return new() { Root = root, CataloguePath = cataloguePath, LogoPath = logoPath, Catalogue = catalogue, Accounts = accounts, Context = context, Store = new(environment, configuration, catalogue, accounts, context) };
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("trophy-publication-lock-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture cleanup path");
            Directory.Delete(resolved, recursive: true);
        }
    }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Trophy.Catalogue.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
