using System.Collections.Concurrent;
using System.Threading.Channels;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed record AnalysisJobSnapshot(string Status, string Message, DateTimeOffset UpdatedAt, int EvidenceCount);
internal sealed record AnalysisQueueRequest(string ClubId, string TrophyId, DateTimeOffset DueAt, long Generation);

public sealed class BackgroundAnalysisQueue(
    CatalogueStore store,
    OpenAiEngravingReader reader,
    MemberMatchingCoordinator matching,
    AccountStore accounts,
    ClubContextAccessor clubContext,
    IConfiguration configuration,
    ILogger<BackgroundAnalysisQueue> logger) : BackgroundService
{
    private readonly Channel<AnalysisQueueRequest> queue = Channel.CreateUnbounded<AnalysisQueueRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, AnalysisJobSnapshot> jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> generations = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan debounce = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("ANALYSIS_DEBOUNCE_SECONDS", 20), 2, 60));

    public AnalysisJobSnapshot Enqueue(string trophyId, int evidenceCount) => Schedule(
        clubContext.RequireClubId(), trophyId, evidenceCount, DateTimeOffset.UtcNow.Add(debounce),
        "Photos saved. Waiting briefly for any more before reading the full set…");

    public AnalysisJobSnapshot EnqueueNow(string trophyId, int evidenceCount) => Schedule(
        clubContext.RequireClubId(), trophyId, evidenceCount, DateTimeOffset.UtcNow, "Reading has been queued…");

    public AnalysisJobSnapshot GetStatus(string trophyId)
    {
        var key = JobKey(clubContext.RequireClubId(), trophyId);
        return jobs.TryGetValue(key, out var status)
            ? status
            : new AnalysisJobSnapshot("idle", "No reading is queued.", DateTimeOffset.UtcNow, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueInterruptedWorkAsync(stoppingToken);
        var pending = new Dictionary<string, AnalysisQueueRequest>(StringComparer.OrdinalIgnoreCase);
        while (!stoppingToken.IsCancellationRequested)
        {
            while (queue.Reader.TryRead(out var request))
            {
                var key = JobKey(request.ClubId, request.TrophyId);
                if (!pending.TryGetValue(key, out var existing) || request.Generation >= existing.Generation)
                    pending[key] = request;
            }

            var due = pending.Where(item => item.Value.DueAt <= DateTimeOffset.UtcNow).OrderBy(item => item.Value.DueAt).ToList();
            foreach (var item in due)
            {
                pending.Remove(item.Key);
                await ProcessAsync(item.Value, stoppingToken);
            }

            if (pending.Count == 0)
            {
                await queue.Reader.WaitToReadAsync(stoppingToken);
                continue;
            }

            var delay = pending.Values.Min(item => item.DueAt) - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            await Task.WhenAny(queue.Reader.WaitToReadAsync(stoppingToken).AsTask(), Task.Delay(delay, stoppingToken));
        }
    }

    private AnalysisJobSnapshot Schedule(string clubId, string trophyId, int evidenceCount, DateTimeOffset dueAt, string message)
    {
        var key = JobKey(clubId, trophyId);
        var generation = generations.AddOrUpdate(key, 1, (_, current) => current + 1);
        var snapshot = new AnalysisJobSnapshot("queued", message, DateTimeOffset.UtcNow, evidenceCount);
        jobs[key] = snapshot;
        queue.Writer.TryWrite(new AnalysisQueueRequest(clubId, trophyId, dueAt, generation));
        return snapshot;
    }

    private async Task ProcessAsync(AnalysisQueueRequest request, CancellationToken cancellationToken)
    {
        var key = JobKey(request.ClubId, request.TrophyId);
        if (HasNewerRequest(request)) return;
        using var clubScope = clubContext.Push(request.ClubId);
        var trophy = await store.GetTrophyAsync(request.TrophyId, cancellationToken);
        var evidenceFiles = await store.GetEvidenceFilesAsync(request.TrophyId, cancellationToken);
        if (trophy is null || evidenceFiles.Count == 0)
        {
            jobs[key] = new AnalysisJobSnapshot("idle", "No images are available to read.", DateTimeOffset.UtcNow, 0);
            return;
        }

        var pendingEvidenceIds = evidenceFiles
            .Where(item => item.Evidence.ProcessingState is ProcessingStates.Pending or "queued" or "processing" or ProcessingStates.Failed)
            .Select(item => item.Evidence.Id)
            .ToList();
        jobs[key] = new AnalysisJobSnapshot("processing", $"Comparing all {evidenceFiles.Count} images…", DateTimeOffset.UtcNow, evidenceFiles.Count);
        foreach (var evidenceId in pendingEvidenceIds)
            await store.SetEvidenceProcessingAsync(request.TrophyId, evidenceId, "processing", "Comparing this with all saved images", cancellationToken);

        try
        {
            var extraction = await reader.ReadAsync(trophy, evidenceFiles, cancellationToken);
            await store.MergeAiExtractionAsync(request.TrophyId, extraction, evidenceFiles.Select(item => item.Evidence.Id).ToList(), cancellationToken);
            await matching.RefreshTrophyAsync(request.TrophyId, cancellationToken);
            var readingMessage = extraction.Entries.Count == 1
                ? $"1 winner reading found across {evidenceFiles.Count} images"
                : $"{extraction.Entries.Count} winner readings found across {evidenceFiles.Count} images";
            foreach (var evidenceId in pendingEvidenceIds)
                await store.SetEvidenceProcessingAsync(request.TrophyId, evidenceId, ProcessingStates.Complete, readingMessage, cancellationToken);
            if (!HasNewerRequest(request)) jobs[key] = new AnalysisJobSnapshot("complete", readingMessage, DateTimeOffset.UtcNow, evidenceFiles.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OpenAiUnavailableException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Background engraving analysis failed for club {ClubId}, trophy {TrophyId}", request.ClubId, request.TrophyId);
            foreach (var evidenceId in pendingEvidenceIds)
                await store.SetEvidenceProcessingAsync(request.TrophyId, evidenceId, ProcessingStates.Failed, exception.Message, cancellationToken);
            if (!HasNewerRequest(request)) jobs[key] = new AnalysisJobSnapshot("failed", exception.Message, DateTimeOffset.UtcNow, evidenceFiles.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected background engraving analysis failure for club {ClubId}, trophy {TrophyId}", request.ClubId, request.TrophyId);
            const string message = "The background reader failed unexpectedly. Try again.";
            foreach (var evidenceId in pendingEvidenceIds)
                await store.SetEvidenceProcessingAsync(request.TrophyId, evidenceId, ProcessingStates.Failed, message, cancellationToken);
            if (!HasNewerRequest(request)) jobs[key] = new AnalysisJobSnapshot("failed", message, DateTimeOffset.UtcNow, evidenceFiles.Count);
        }
    }

    private bool HasNewerRequest(AnalysisQueueRequest request)
    {
        var key = JobKey(request.ClubId, request.TrophyId);
        return generations.TryGetValue(key, out var current) && current > request.Generation;
    }

    private async Task RequeueInterruptedWorkAsync(CancellationToken cancellationToken)
    {
        foreach (var clubId in await accounts.GetClubIdsAsync(cancellationToken))
        {
            using var clubScope = clubContext.Push(clubId);
            var summaries = await store.GetSummariesAsync(cancellationToken);
            foreach (var summary in summaries)
            {
                var trophy = await store.GetTrophyAsync(summary.Id, cancellationToken);
                if (trophy?.Evidence.Any(item => item.ProcessingState is ProcessingStates.Pending or "queued" or "processing") == true)
                    Schedule(clubId, trophy.Id, trophy.Evidence.Count, DateTimeOffset.UtcNow.Add(debounce), "Resuming the saved background reading…");
            }
        }
    }

    private static string JobKey(string clubId, string trophyId) => $"{clubId}:{trophyId}";
}
