using System.Collections.Concurrent;
using System.Threading.Channels;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed record IllustrationJobSnapshot(string Status, string Message, DateTimeOffset UpdatedAt);
internal sealed record IllustrationQueueRequest(string ClubId, string TrophyId);

public sealed class BackgroundIllustrationQueue(
    CatalogueStore store,
    OpenAiTrophyIllustrator illustrator,
    AccountStore accounts,
    ClubContextAccessor clubContext,
    ILogger<BackgroundIllustrationQueue> logger) : BackgroundService
{
    private readonly Channel<IllustrationQueueRequest> queue = Channel.CreateUnbounded<IllustrationQueueRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, IllustrationJobSnapshot> jobs = new(StringComparer.OrdinalIgnoreCase);

    public IllustrationJobSnapshot Enqueue(string trophyId)
    {
        var clubId = clubContext.RequireClubId();
        var key = JobKey(clubId, trophyId);
        var snapshot = new IllustrationJobSnapshot(
            "queued",
            "Photographs saved. The catalogue illustration will be created in the background.",
            DateTimeOffset.UtcNow);
        jobs[key] = snapshot;
        queue.Writer.TryWrite(new IllustrationQueueRequest(clubId, trophyId));
        return snapshot;
    }

    public IllustrationJobSnapshot GetStatus(string trophyId)
    {
        var key = JobKey(clubContext.RequireClubId(), trophyId);
        return jobs.TryGetValue(key, out var snapshot)
            ? snapshot
            : new IllustrationJobSnapshot("idle", "No illustration is queued.", DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueInterruptedWorkAsync(stoppingToken);
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            await ProcessAsync(request, stoppingToken);
    }

    private async Task ProcessAsync(IllustrationQueueRequest request, CancellationToken cancellationToken)
    {
        var key = JobKey(request.ClubId, request.TrophyId);
        using var clubScope = clubContext.Push(request.ClubId);
        var trophy = await store.GetTrophyAsync(request.TrophyId, cancellationToken);
        if (trophy is null)
        {
            jobs[key] = new IllustrationJobSnapshot("failed", "The trophy record no longer exists.", DateTimeOffset.UtcNow);
            return;
        }

        var references = await store.GetTrophyPhotoFilesAsync(request.TrophyId, cancellationToken);
        if (references.Count == 0)
        {
            const string message = "Add at least one trophy reference photograph before creating an illustration.";
            await store.SetIllustrationStatusAsync(request.TrophyId, IllustrationStates.Failed, message, cancellationToken);
            jobs[key] = new IllustrationJobSnapshot("failed", message, DateTimeOffset.UtcNow);
            return;
        }

        jobs[key] = new IllustrationJobSnapshot("processing", "Creating the catalogue illustration from the saved angles…", DateTimeOffset.UtcNow);
        await store.SetIllustrationStatusAsync(request.TrophyId, IllustrationStates.Processing, "Creating the catalogue illustration in the background…", cancellationToken);
        try
        {
            var image = await illustrator.GenerateAsync(trophy.Name, references, cancellationToken);
            await store.SaveIllustrationAsync(request.TrophyId, image, cancellationToken);
            jobs[key] = new IllustrationJobSnapshot("complete", "Catalogue illustration created.", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OpenAiUnavailableException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Background illustration failed for club {ClubId}, trophy {TrophyId}", request.ClubId, request.TrophyId);
            await store.SetIllustrationStatusAsync(request.TrophyId, IllustrationStates.Failed, exception.Message, cancellationToken);
            jobs[key] = new IllustrationJobSnapshot("failed", exception.Message, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected background illustration failure for club {ClubId}, trophy {TrophyId}", request.ClubId, request.TrophyId);
            const string message = "The illustration could not be completed. Your trophy and photographs are safe.";
            await store.SetIllustrationStatusAsync(request.TrophyId, IllustrationStates.Failed, message, cancellationToken);
            jobs[key] = new IllustrationJobSnapshot("failed", message, DateTimeOffset.UtcNow);
        }
    }

    private async Task RequeueInterruptedWorkAsync(CancellationToken cancellationToken)
    {
        if (!illustrator.IsAvailable) return;
        foreach (var clubId in await accounts.GetClubIdsAsync(cancellationToken))
        {
            using var clubScope = clubContext.Push(clubId);
            foreach (var summary in await store.GetSummariesAsync(cancellationToken))
            {
                var trophy = await store.GetTrophyAsync(summary.Id, cancellationToken);
                if (trophy?.IllustrationState == IllustrationStates.Processing)
                    queue.Writer.TryWrite(new IllustrationQueueRequest(clubId, trophy.Id));
            }
        }
    }

    private static string JobKey(string clubId, string trophyId) => $"{clubId}:{trophyId}";
}
