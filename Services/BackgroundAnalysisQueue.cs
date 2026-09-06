using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed record AnalysisJobSnapshot(string Status, string Message, DateTimeOffset UpdatedAt, int EvidenceCount);

public sealed class BackgroundAnalysisQueue(
    CatalogueStore store,
    OpenAiEngravingReader reader,
    MemberMatchingCoordinator matching,
    AccountStore accounts,
    ClubContextAccessor clubContext,
    BillingStore billing,
    IConfiguration configuration,
    ILogger<BackgroundAnalysisQueue> logger) : BackgroundService
{
    private readonly TimeSpan debounce = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("ANALYSIS_DEBOUNCE_SECONDS", 20), 2, 60));

    public AnalysisJobSnapshot Enqueue(string trophyId, int evidenceCount) => Schedule(trophyId, evidenceCount, DateTimeOffset.UtcNow.Add(debounce));
    public AnalysisJobSnapshot EnqueueNow(string trophyId, int evidenceCount) => Schedule(trophyId, evidenceCount, DateTimeOffset.UtcNow);
    private AnalysisJobSnapshot Schedule(string trophyId, int evidenceCount, DateTimeOffset dueAt)
    {
        var clubId = clubContext.RequireClubId();
        billing.EnsureClub(clubId, clubId == "legacy" && accounts.LegacyArchiveExists);
        return Snapshot(billing.ScheduleJob(clubId, trophyId, "analysis", evidenceCount, dueAt));
    }
    public AnalysisJobSnapshot GetStatus(string trophyId)
    {
        var job = billing.JobStatus(clubContext.RequireClubId(), trophyId, "analysis");
        return job is null ? new("idle", "No reading is queued.", DateTimeOffset.UtcNow, 0) : Snapshot(job);
    }
    private static AnalysisJobSnapshot Snapshot(DurableBillableJob job) => new(job.State == "running" ? "processing" : job.State, job.Message, job.UpdatedAt, job.EvidenceCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = billing.NextJob("analysis");
                if (job != null) { await ProcessAsync(job, stoppingToken); continue; }
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not poll the durable analysis queue.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(DurableBillableJob job, CancellationToken cancellationToken)
    {
        using var scope = clubContext.Push(job.ClubId);
        var started = false;
        var evidenceIds = new List<string>();
        try
        {
            var trophy = await store.GetTrophyAsync(job.TrophyId, cancellationToken);
            var files = await store.GetEvidenceFilesAsync(job.TrophyId, cancellationToken);
            if (trophy is null || files.Count == 0 || !reader.IsAvailable)
            {
                billing.FailJob(job, trophy is null ? "The trophy no longer exists." : files.Count == 0 ? "Add photographs before requesting a reading." : "The AI reader is not configured. Your photographs are saved.", false);
                return;
            }
            evidenceIds = files.Select(x => x.Evidence.Id).ToList();
            foreach (var id in evidenceIds) await store.SetEvidenceProcessingAsync(job.TrophyId, id, "processing", "Comparing the saved images", cancellationToken);
            started = billing.BeginProviderAttempt(job, trophy.Evidence.Count + trophy.TrophyPhotos.Count);
            if (!started) return;
            var extraction = await reader.ReadAsync(trophy, files, cancellationToken);
            await store.MergeAiExtractionAsync(job.TrophyId, extraction, evidenceIds, cancellationToken);
            await matching.RefreshTrophyAsync(job.TrophyId, cancellationToken);
            var message = $"{extraction.Entries.Count} winner readings found across {files.Count} images";
            foreach (var id in evidenceIds) await store.SetEvidenceProcessingAsync(job.TrophyId, id, ProcessingStates.Complete, message, cancellationToken);
            billing.CompleteJob(job, message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Analysis job {JobId} stopped for club {ClubId}, trophy {TrophyId}", job.Id, job.ClubId, job.TrophyId);
            var message = started ? "The provider outcome needs review before another attempt. Your trophy and photographs are safe; contact support." : exception is BillingException billingException ? billingException.Message : "This reading could not start. Your photographs are safe.";
            billing.FailJob(job, message, started);
            foreach (var id in evidenceIds)
                try { await store.SetEvidenceProcessingAsync(job.TrophyId, id, ProcessingStates.Failed, message, CancellationToken.None); } catch (Exception updateException) { logger.LogWarning(updateException, "Could not save evidence job status for {JobId}", job.Id); }
            if (cancellationToken.IsCancellationRequested) throw;
        }
    }
}
