using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

/// <summary>Finite operational limits for archive growth. Existing content is never removed to meet a limit.</summary>
public sealed class ArchiveResourceLimits(IConfiguration configuration)
{
    // All stores that grow the shared archive disk use this gate for check-and-write.
    public static SemaphoreSlim StorageGate { get; } = new(1, 1);
    private const long MiB = 1024L * 1024;
    public long FreeStorageBytes { get; } = Positive(configuration, "ARCHIVE_FREE_STORAGE_BYTES", 256 * MiB);
    public long PaidStorageBytes { get; } = Positive(configuration, "ARCHIVE_PAID_STORAGE_BYTES", 2048 * MiB);
    public long SharedStorageBytes { get; } = Positive(configuration, "ARCHIVE_SHARED_STORAGE_BYTES", 4096 * MiB);
    public long MinimumFreeDiskBytes { get; } = Positive(configuration, "ARCHIVE_MIN_FREE_DISK_BYTES", 128 * MiB);
    public long UploadBytes { get; } = Positive(configuration, "ARCHIVE_MAX_UPLOAD_BYTES", 12 * MiB);
    public long IllustrationBytes { get; } = Positive(configuration, "ARCHIVE_MAX_ILLUSTRATION_BYTES", 32 * MiB);
    public long StateBytes { get; } = Positive(configuration, "ARCHIVE_MAX_STATE_BYTES", 16 * MiB);
    public int DraftTrophies { get; } = checked((int)Positive(configuration, "ARCHIVE_DRAFT_TROPHIES", 4));
    public int WinnersPerTrophy { get; } = checked((int)Positive(configuration, "ARCHIVE_MAX_WINNERS_PER_TROPHY", 4000));
    public int WinnersPerArchive { get; } = checked((int)Positive(configuration, "ARCHIVE_MAX_WINNERS", 100000));

    public ArchiveAllowance Allowance(BillingBalance? balance)
    {
        // Settled credits move out of Available, so include both allocated credit states.
        var credits = balance is null ? 1 : Math.Max(0, balance.Available + balance.Reserved + balance.Used);
        var unlimited = balance?.Unlimited == true;
        return new(unlimited, unlimited ? long.MaxValue : Math.Max(1, credits) + DraftTrophies,
            unlimited ? long.MaxValue : credits > 1 ? PaidStorageBytes : FreeStorageBytes);
    }

    public void CheckTrophyCount(int count, ArchiveAllowance allowance)
    {
        if (!allowance.Unlimited && count > allowance.Trophies)
            throw new BillingException("trophy_limit", "This archive has reached its trophy allowance. Add trophy credits before creating another trophy.", 402);
    }

    public void ValidateState(CatalogueState state, CatalogueState previous, ArchiveAllowance allowance)
    {
        if (state.Trophies.Count > previous.Trophies.Count) CheckTrophyCount(state.Trophies.Count, allowance);
        var oldTrophies = previous.Trophies.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var totalWinners = state.Trophies.Sum(item => (long)item.Winners.Count);
        var oldTotalWinners = previous.Trophies.Sum(item => (long)item.Winners.Count);
        if (totalWinners > WinnersPerArchive && totalWinners > oldTotalWinners)
            throw new BillingException("winner_limit", "This archive has reached its winner-record limit. Contact support before adding more records.", 413);
        foreach (var trophy in state.Trophies)
        {
            oldTrophies.TryGetValue(trophy.Id, out var old);
            if (trophy.Winners.Count > WinnersPerTrophy && trophy.Winners.Count > (old?.Winners.Count ?? 0))
                throw new BillingException("winner_limit", "This trophy has reached its winner-record limit. Contact support before adding more records.", 413);
            CheckText(trophy.Name, old?.Name, 300, "Trophy name");
            CheckText(trophy.SecondaryName, old?.SecondaryName, 300, "Trophy subtitle");
            CheckText(trophy.Category, old?.Category, 120, "Trophy category");
            CheckText(trophy.EngravingInstructions, old?.EngravingInstructions, 12000, "Engraving instructions");
            var oldWinners = old?.Winners.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var winner in trophy.Winners)
            {
                oldWinners.TryGetValue(winner.Id, out var oldWinner);
                CheckText(winner.Name, oldWinner?.Name, 300, "Winner name");
                CheckText(winner.Description, oldWinner?.Description, 12000, "Winner description");
                CheckText(winner.ExtractionNotes, oldWinner?.ExtractionNotes, 12000, "Reading notes");
            }
        }
    }

    public static void CheckText(string? value, string? previous, int maximum, string label)
    {
        if ((value?.Length ?? 0) > maximum && (value?.Length ?? 0) > (previous?.Length ?? 0))
            throw new BillingException("metadata_limit", $"{label} must be no longer than {maximum:N0} characters.", 413);
    }

    public static void ValidateUploadMetadata(string originalName, string contentType)
    {
        CheckText(Path.GetFileName(originalName), null, 255, "Photograph filename");
        if (contentType is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new BillingException("invalid_image_type", "Use JPEG, PNG or WebP images.", 400);
    }

    public long RemainingFileBytes(string dataRoot, string tenantRoot, ArchiveAllowance allowance)
    {
        var tenantRemaining = allowance.Unlimited ? long.MaxValue : allowance.StorageBytes - DirectoryBytes(tenantRoot);
        var sharedRemaining = SharedStorageBytes - DirectoryBytes(dataRoot);
        var physicalRemaining = AvailableDiskBytes(dataRoot) - MinimumFreeDiskBytes;
        if (tenantRemaining <= 0) throw TenantStorageFull();
        if (Math.Min(sharedRemaining, physicalRemaining) <= 0) throw SharedStorageFull();
        return Math.Min(tenantRemaining, Math.Min(sharedRemaining, physicalRemaining));
    }

    public void CheckWrite(string dataRoot, string tenantRoot, ArchiveAllowance allowance, long bytes, long replacedBytes = 0)
    {
        var growth = Math.Max(0, bytes - replacedBytes);
        if (growth > 0 && !allowance.Unlimited && DirectoryBytes(tenantRoot) + growth > allowance.StorageBytes)
            throw TenantStorageFull();
        if (growth > 0 && DirectoryBytes(dataRoot) + growth > SharedStorageBytes) throw SharedStorageFull();
        // Atomic replacement needs room for the complete temporary file, not just its net growth.
        if (AvailableDiskBytes(dataRoot) - bytes < MinimumFreeDiskBytes) throw SharedStorageFull();
    }

    public static long DirectoryBytes(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateFiles(directory, "*", options).Sum(path => new FileInfo(path).Length);
    }

    private static long AvailableDiskBytes(string directory)
    {
        var fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var drive = DriveInfo.GetDrives().Where(item => item.IsReady)
            .Where(item => fullPath.StartsWith(Path.GetFullPath(item.Name).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        return drive?.AvailableFreeSpace ?? throw SharedStorageFull();
    }

    public static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (read > maximum - written)
                throw new BillingException("upload_limit", "This image exceeds the available upload or archive storage allowance.", 413);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
        }
        if (written == 0) throw new BillingException("empty_image", "Choose an image that contains data.", 400);
    }

    private static long Positive(IConfiguration configuration, string key, long fallback)
    {
        var value = configuration.GetValue<long?>(key) ?? fallback;
        if (value <= 0) throw new InvalidOperationException($"{key} must be a positive number.");
        return value;
    }
    private static BillingException TenantStorageFull() => new("storage_limit", "This archive has reached its storage allowance. Contact support before uploading more photographs.", 413);
    private static BillingException SharedStorageFull() => new("storage_capacity", "Uploads are temporarily paused because archive storage is nearly full. Your saved archive remains available.", 503);
}

public sealed record ArchiveAllowance(bool Unlimited, long Trophies, long StorageBytes);

internal sealed class BoundedArchiveStream(long maximum) : MemoryStream
{
    private void Check(int count)
    {
        if (count > maximum - Position)
            throw new BillingException("archive_state_limit", "This archive has reached its record storage limit. Contact support before adding more records.", 413);
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
}
