using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class ArchiveResourceLimitTests
{
    [Fact]
    public async Task ConcurrentFreeTrophyCreationCannotExceedCreditAndDraftAllowance()
    {
        using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Context.Push("club-a");
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
        {
            try { await fixture.Store.CreateTrophyAsync(new($"Cup {index}", null, "Golf", null, null)); return true; }
            catch (BillingException error) when (error.Code == "trophy_limit") { return false; }
        }));
        Assert.Equal(5, results.Count(success => success));
        Assert.Equal(5, (await fixture.Store.GetTrophiesAsync()).Count);
        Assert.Equal(5, fixture.ReadSavedState("club-a").Trophies.Count);
    }

    [Fact]
    public async Task PurchasedAndSpentCreditsRetainTheFullTrophyAllowance()
    {
        using var fixture = await Fixture.CreateAsync();
        var purchase = fixture.Billing.CreatePurchase("club-a", new("complete", Guid.NewGuid().ToString()));
        fixture.Billing.FulfilPayment("event", purchase.Id, "checkout", "payment", purchase.AmountPence, "gbp", "customer");
        var limits = new ArchiveResourceLimits(fixture.Configuration);
        Assert.Equal(255, limits.Allowance(fixture.Billing.Balance("club-a")).Trophies);
        // Settled credits are still represented by the original trophy records.
        Assert.Equal(255, limits.Allowance(new("club-a", false, 0, 1, 250, false, null)).Trophies);
        Assert.Equal(2048L * 1024 * 1024, limits.Allowance(fixture.Billing.Balance("club-a")).StorageBytes);
    }

    [Fact]
    public async Task ConcurrentEvidenceAndReferencePhotosShareAnAtomicAllowance()
    {
        using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Context.Push("club-a");
        var trophy = await fixture.CreateTrophyAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(async index =>
        {
            try
            {
                using var content = new MemoryStream([1, 2, 3]);
                if (index % 2 == 0) await fixture.Store.AddEvidenceAsync(trophy.Id, "photo.png", "image/png", EvidenceKinds.Photo, content);
                else await fixture.Store.AddTrophyPhotoAsync(trophy.Id, "photo.png", "image/png", content);
                return true;
            }
            catch (BillingException error) when (error.Code == "photo_limit") { return false; }
        }));
        Assert.Equal(12, results.Count(success => success));
        var saved = (await fixture.Store.GetTrophyAsync(trophy.Id))!;
        Assert.Equal(12, saved.Evidence.Count + saved.TrophyPhotos.Count);
        Assert.Equal(12, Directory.EnumerateFiles(fixture.ClubRoot("club-a"), "*.png", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task NonSeekableOversizedUploadLeavesNoFileOrMetadata()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_MAX_UPLOAD_BYTES"] = "32" });
        using var scope = fixture.Context.Push("club-a");
        var trophy = await fixture.CreateTrophyAsync();
        var before = File.ReadAllBytes(fixture.StatePath("club-a"));
        using var content = new NonSeekableStream(new byte[33]);
        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.Store.AddEvidenceAsync(trophy.Id, "large.png", "image/png", EvidenceKinds.Photo, content));
        Assert.Equal("upload_limit", error.Code);
        Assert.Empty((await fixture.Store.GetTrophyAsync(trophy.Id))!.Evidence);
        Assert.Empty(Directory.EnumerateFiles(fixture.ClubRoot("club-a"), "*.png", SearchOption.AllDirectories));
        Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-a")));
    }

    [Fact]
    public async Task ConcurrentUploadsCannotExceedOneClubsByteQuota()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_FREE_STORAGE_BYTES"] = "4096" });
        using var scope = fixture.Context.Push("club-a");
        var trophy = await fixture.CreateTrophyAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var content = new MemoryStream(new byte[2500]);
            try { await fixture.Store.AddEvidenceAsync(trophy.Id, "image.png", "image/png", EvidenceKinds.Photo, content); return true; }
            catch (BillingException error) when (error.Code is "upload_limit" or "storage_limit") { return false; }
        }));
        Assert.Single(results, success => success);
        Assert.Single((await fixture.Store.GetTrophyAsync(trophy.Id))!.Evidence);
        Assert.True(ArchiveResourceLimits.DirectoryBytes(fixture.ClubRoot("club-a")) <= 4096);
    }

    [Fact]
    public async Task TwoClubsCannotRacePastSharedDiskCapacity()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_SHARED_STORAGE_BYTES"] = "10000" });
        await fixture.InClub("club-a", () => fixture.CreateTrophyAsync());
        await fixture.InClub("club-b", () => fixture.CreateTrophyAsync());
        var results = await Task.WhenAll(new[] { "club-a", "club-b" }.Select(club => fixture.InClub(club, async () =>
        {
            using var content = new MemoryStream(new byte[5000]);
            try { await fixture.Store.AddEvidenceAsync("CUP", "photo.png", "image/png", EvidenceKinds.Photo, content); return true; }
            catch (BillingException error) when (error.Code is "upload_limit" or "storage_capacity") { return false; }
        })));
        Assert.Single(results, success => success);
        Assert.True(ArchiveResourceLimits.DirectoryBytes(fixture.DataRoot) <= 10000);
        Assert.Single(Directory.EnumerateFiles(fixture.DataRoot, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingOverLimitArchiveAndIllustrationRemainReadableWithoutRewriting()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_FREE_STORAGE_BYTES"] = "1", ["ARCHIVE_MAX_STATE_BYTES"] = "1" });
        var trophy = OriginalTrophy();
        fixture.Seed("club-a", new() { Trophies = [trophy] });
        var illustration = fixture.WriteIllustration("club-a", trophy.Id, [0, 42, 128, 255]);
        var stateBytes = File.ReadAllBytes(fixture.StatePath("club-a"));
        using var scope = fixture.Context.Push("club-a");
        Assert.Single(await fixture.Store.GetTrophiesAsync());
        Assert.Equal(illustration, await fixture.Store.GetIllustrationPathAsync(trophy.Id));
        using var content = new MemoryStream([5]);
        await Assert.ThrowsAsync<BillingException>(() => fixture.Store.AddTrophyPhotoAsync(trophy.Id, "new.png", "image/png", content));
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.StatePath("club-a")));
        Assert.Equal(new byte[] { 0, 42, 128, 255 }, File.ReadAllBytes(illustration));
    }

    [Fact]
    public async Task TrustedUnlimitedClubRetainsTrophyPhotoAndTenantStorageExemption()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_FREE_STORAGE_BYTES"] = "1" });
        fixture.Billing.EnsureClub("club-a", trustedUnlimited: true);
        using var scope = fixture.Context.Push("club-a");
        for (var index = 0; index < 7; index++) await fixture.Store.CreateTrophyAsync(new($"Cup {index}", null, "Golf", $"C{index}", null));
        for (var index = 0; index < 13; index++)
        {
            using var content = new MemoryStream([1, 2, 3]);
            await fixture.Store.AddTrophyPhotoAsync("C0", "photo.png", "image/png", content);
        }
        Assert.Equal(7, (await fixture.Store.GetTrophiesAsync()).Count);
        Assert.Equal(13, (await fixture.Store.GetTrophyAsync("C0"))!.TrophyPhotos.Count);
        Assert.True(fixture.Billing.Balance("club-a").Unlimited);
    }

    [Fact]
    public async Task RejectedWinnerMetadataRollsBackBothMemoryAndDisk()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_MAX_STATE_BYTES"] = "1100" });
        using var scope = fixture.Context.Push("club-a");
        var trophy = await fixture.CreateTrophyAsync();
        var before = File.ReadAllBytes(fixture.StatePath("club-a"));
        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.Store.AddWinnerAsync(trophy.Id, new(2000, "Winner", ReviewStates.Confirmed, new string('x', 900))));
        Assert.Equal("archive_state_limit", error.Code);
        Assert.Empty((await fixture.Store.GetTrophyAsync(trophy.Id))!.Winners);
        Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-a")));
        Assert.Empty(Directory.EnumerateFiles(fixture.DataRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AiMergeCannotExceedWinnerLimitOrDiscardExistingConfirmedRecords()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_MAX_WINNERS_PER_TROPHY"] = "2" });
        using var scope = fixture.Context.Push("club-a");
        var trophy = await fixture.CreateTrophyAsync();
        await fixture.Store.AddWinnerAsync(trophy.Id, new(2000, "Confirmed Winner", ReviewStates.Confirmed, null));
        var before = File.ReadAllBytes(fixture.StatePath("club-a"));
        var extraction = new AiExtraction { Entries = [new() { Year = 2001, Winner = "One" }, new() { Year = 2002, Winner = "Two" }] };
        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.Store.MergeAiExtractionAsync(trophy.Id, extraction, []));
        Assert.Equal("winner_limit", error.Code);
        Assert.Equal("Confirmed Winner", Assert.Single((await fixture.Store.GetTrophyAsync(trophy.Id))!.Winners).Name);
        Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-a")));
    }

    [Fact]
    public async Task IllustrationMetadataRejectionRestoresOriginalBytesAtomically()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_MAX_STATE_BYTES"] = "1" });
        fixture.Seed("club-a", new() { Trophies = [OriginalTrophy()] });
        var illustration = fixture.WriteIllustration("club-a", "CUP", [0, 42, 128, 255]);
        var before = File.ReadAllBytes(fixture.StatePath("club-a"));
        using var scope = fixture.Context.Push("club-a");
        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.Store.SaveIllustrationAsync("CUP", [9, 9, 9]));
        Assert.Equal("archive_state_limit", error.Code);
        Assert.Equal(new byte[] { 0, 42, 128, 255 }, File.ReadAllBytes(illustration));
        Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-a")));
        Assert.Equal(1, (await fixture.Store.GetTrophyAsync("CUP"))!.IllustrationGenerationCount);
        Assert.Empty(Directory.EnumerateFiles(fixture.ClubRoot("club-a"), "*.previous", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LockedOriginalIllustrationIsNeverRemovedByFailedReplacement()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await Fixture.CreateAsync();
        fixture.Seed("club-a", new() { Trophies = [OriginalTrophy()] });
        var illustration = fixture.WriteIllustration("club-a", "CUP", [1, 42, 128, 255]);
        using var scope = fixture.Context.Push("club-a");
        await using (var reader = new FileStream(illustration, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => fixture.Store.SaveIllustrationAsync("CUP", [9, 9, 9]));
        Assert.Equal(new byte[] { 1, 42, 128, 255 }, File.ReadAllBytes(illustration));
        Assert.Equal(1, (await fixture.Store.GetTrophyAsync("CUP"))!.IllustrationGenerationCount);
    }

    [Fact]
    public async Task CancelledSharedGateWaitRestoresMutationBeforeAnotherSave()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.InClub("club-a", () => fixture.CreateTrophyAsync());
        await fixture.InClub("club-b", () => fixture.CreateTrophyAsync());
        var before = File.ReadAllBytes(fixture.StatePath("club-b"));
        using var upload = new BlockingStream();
        var active = fixture.InClub("club-a", () => fixture.Store.AddTrophyPhotoAsync("CUP", "photo.png", "image/png", upload));
        await upload.Started.Task;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var mutation = fixture.InClub("club-b", () => fixture.Store.AddWinnerAsync("CUP", new(2000, "Cancelled winner", ReviewStates.Confirmed, null), cancellation.Token));
            Assert.False(mutation.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mutation);
            Assert.Empty((await fixture.InClub("club-b", () => fixture.Store.GetTrophyAsync("CUP")))!.Winners);
            Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-b")));
        }
        finally { upload.Release.TrySetResult(); await active; }
        await fixture.InClub("club-b", () => fixture.Store.AddWinnerAsync("CUP", new(2001, "Saved winner", ReviewStates.Confirmed, null)));
        Assert.Equal("Saved winner", Assert.Single(fixture.ReadSavedState("club-b").Trophies[0].Winners).Name);
    }

    [Fact]
    public async Task RejectedPhotoDeletionKeepsItsOriginalFileAndRecord()
    {
        using var fixture = await Fixture.CreateAsync(new() { ["ARCHIVE_MIN_FREE_DISK_BYTES"] = long.MaxValue.ToString() });
        var photo = new EvidenceImage { Id = "original", OriginalName = "original.png", ContentType = "image/png" };
        fixture.Seed("club-a", new() { Trophies = [new() { Id = "CUP", Name = "Cup", Category = "Golf", TrophyPhotos = [photo] }] });
        var directory = Path.Combine(fixture.ClubRoot("club-a"), "trophy-photos", "CUP");
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "original.png");
        File.WriteAllBytes(image, [7, 42, 128, 255]);
        var before = File.ReadAllBytes(fixture.StatePath("club-a"));
        using var scope = fixture.Context.Push("club-a");
        await Assert.ThrowsAsync<BillingException>(() => fixture.Store.DeleteTrophyPhotoAsync("CUP", "original"));
        Assert.Equal(new byte[] { 7, 42, 128, 255 }, File.ReadAllBytes(image));
        Assert.Single((await fixture.Store.GetTrophyAsync("CUP"))!.TrophyPhotos);
        Assert.Equal(before, File.ReadAllBytes(fixture.StatePath("club-a")));
    }

    [Fact]
    public async Task LegacyLongMetadataSurvivesWinnerReorderingWithoutBeingExpanded()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Seed("club-a", new() { Trophies = [new() { Id = "CUP", Name = "Cup", Category = "Golf", Winners = [new() { Id = "old", Year = 2001, Name = "Original", ExtractionNotes = new string('x', 17000) }] }] });
        using var scope = fixture.Context.Push("club-a");
        await fixture.Store.AddWinnerAsync("CUP", new(2000, "Earlier winner", ReviewStates.Confirmed, null));
        Assert.Equal(17000, (await fixture.Store.GetTrophyAsync("CUP"))!.Winners.Single(winner => winner.Id == "old").ExtractionNotes!.Length);
    }

    private static TrophyRecord OriginalTrophy() => new()
    {
        Id = "CUP", Name = "Original Cup", Category = "Golf", IllustrationState = IllustrationStates.Complete,
        IllustrationGenerationCount = 1, ReferenceImage = "/api/trophies/CUP/illustration"
    };

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "trophy-resource-tests", Guid.NewGuid().ToString("N"));
        public string DataRoot => Path.Combine(Root, "data");
        public IConfigurationRoot Configuration { get; private set; } = null!;
        public BillingStore Billing { get; private set; } = null!;
        public CatalogueStore Store { get; private set; } = null!;
        public ClubContextAccessor Context { get; } = new();
        private Fixture() { }
        public static async Task<Fixture> CreateAsync(Dictionary<string, string?>? settings = null)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.DataRoot);
            settings ??= [];
            settings["DATA_PATH"] = fixture.DataRoot;
            settings["SKIP_SEED_CATALOGUE"] = "true";
            settings.TryAdd("ARCHIVE_MIN_FREE_DISK_BYTES", "1");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var billing = new BillingStore(Path.Combine(fixture.Root, "operations.sqlite"));
            await billing.InitializeAsync();
            billing.EnsureClub("club-a"); billing.EnsureClub("club-b");
            fixture.Configuration = configuration;
            fixture.Billing = billing;
            fixture.Store = new CatalogueStore(new TestEnvironment(fixture.Root), configuration, fixture.Context, billing);
            return fixture;
        }
        public string ClubRoot(string club) => AppDataPath.ClubRoot(DataRoot, club);
        public string StatePath(string club) => Path.Combine(ClubRoot(club), "catalogue-state.json");
        public Task<TrophyRecord> CreateTrophyAsync() => Store.CreateTrophyAsync(new("Cup", null, "Golf", "CUP", null));
        public async Task<T> InClub<T>(string club, Func<Task<T>> action) { using var scope = Context.Push(club); return await action(); }
        public void Seed(string club, CatalogueState state)
        {
            Directory.CreateDirectory(ClubRoot(club));
            File.WriteAllText(StatePath(club), JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        public CatalogueState ReadSavedState(string club) => JsonSerializer.Deserialize<CatalogueState>(File.ReadAllText(StatePath(club)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        public string WriteIllustration(string club, string trophy, byte[] bytes)
        {
            var directory = Path.Combine(ClubRoot(club), "illustrations"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, trophy + ".png"); File.WriteAllBytes(path, bytes); return path;
        }
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "trophy-resource-tests")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path");
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

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (sent) return 0;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            buffer.Span[0] = 1; sent = true; return 1;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
