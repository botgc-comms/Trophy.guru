using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed record IllustrationJobSnapshot(string Status, string Message, DateTimeOffset UpdatedAt);

public sealed class BackgroundIllustrationQueue(
    CatalogueStore store,
    OpenAiTrophyIllustrator illustrator,
    AccountStore accounts,
    ClubContextAccessor clubContext,
    BillingStore billing,
    ILogger<BackgroundIllustrationQueue> logger) : BackgroundService
{
    public IllustrationJobSnapshot Enqueue(string trophyId)
    {
        var clubId = clubContext.RequireClubId();
        billing.EnsureClub(clubId, clubId == "legacy" && accounts.LegacyArchiveExists);
        return Snapshot(billing.ScheduleJob(clubId, trophyId, "illustration", 0, DateTimeOffset.UtcNow));
    }
    public IllustrationJobSnapshot GetStatus(string trophyId)
    {
        var job = billing.JobStatus(clubContext.RequireClubId(), trophyId, "illustration");
        return job is null ? new("idle", "No illustration is queued.", DateTimeOffset.UtcNow) : Snapshot(job);
    }
    private static IllustrationJobSnapshot Snapshot(DurableBillableJob job) => new(job.State == "running" ? "processing" : job.State, job.Message, job.UpdatedAt);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = billing.NextJob("illustration");
                if (job != null) { await ProcessAsync(job, stoppingToken); continue; }
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not poll the durable illustration queue.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(DurableBillableJob job, CancellationToken cancellationToken)
    {
        using var scope = clubContext.Push(job.ClubId);
        var started = false;
        try
        {
            var trophy = await store.GetTrophyAsync(job.TrophyId, cancellationToken);
            var references = await store.GetTrophyPhotoFilesAsync(job.TrophyId, cancellationToken);
            if (trophy is null || references.Count == 0 || !illustrator.IsAvailable)
            {
                billing.FailJob(job, trophy is null ? "The trophy no longer exists." : references.Count == 0 ? "Add a trophy reference photograph first." : "The illustration generator is not configured. Your photographs are saved.", false);
                return;
            }
            await store.SetIllustrationStatusAsync(job.TrophyId, IllustrationStates.Processing, "Creating the catalogue illustration from the saved photographs…", cancellationToken);
            started = billing.BeginProviderAttempt(job, trophy.TrophyPhotos.Count + trophy.Evidence.Count);
            if (!started) return;
            var image = await illustrator.GenerateAsync(trophy.Name, references, cancellationToken);
            await store.SaveIllustrationAsync(job.TrophyId, image, cancellationToken);
            billing.CompleteJob(job, "Catalogue illustration created.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Illustration job {JobId} stopped for club {ClubId}, trophy {TrophyId}", job.Id, job.ClubId, job.TrophyId);
            var message = started ? "The provider outcome needs review before another attempt. Your trophy and photographs are safe; contact support." : exception is BillingException billingException ? billingException.Message : "This illustration could not start. Your photographs are safe.";
            billing.FailJob(job, message, started);
            try { await store.SetIllustrationStatusAsync(job.TrophyId, IllustrationStates.Failed, message, CancellationToken.None); } catch (Exception updateException) { logger.LogWarning(updateException, "Could not save illustration job status for {JobId}", job.Id); }
            if (cancellationToken.IsCancellationRequested) throw;
        }
    }
}
