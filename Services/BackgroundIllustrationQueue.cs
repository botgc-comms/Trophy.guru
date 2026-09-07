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
    private readonly object activeGate = new();
    private readonly Dictionary<(string Club, string Trophy), ActiveRequest> activeRequests = new();
    private sealed record ActiveRequest(CancellationTokenSource Cancellation, TaskCompletionSource Finished);

    public async Task<IllustrationJobSnapshot> RestartAsync(string trophyId, CancellationToken cancellationToken)
    {
        var clubId = clubContext.RequireClubId();
        Task? previous = null;
        lock (activeGate) {
            billing.CancelIllustrationJobs(clubId, trophyId);
            if (activeRequests.TryGetValue((clubId, trophyId), out var active)) {
                active.Cancellation.Cancel();
                previous = active.Finished.Task;
            }
        }
        if (previous is not null) {
            try { await previous.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            catch (TimeoutException) { throw new BillingException("image_stopping", "The previous image request is stopping. Please try again in a few seconds."); }
        }
        return Enqueue(trophyId);
    }

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

    public async Task<TrophyRecord?> ReconcileAsync(TrophyRecord? trophy, CancellationToken cancellationToken = default)
    {
        if (trophy is null || trophy.IllustrationState != IllustrationStates.Processing) return trophy;
        var job = GetStatus(trophy.Id);
        if (job.Status is "queued" or "processing") return trophy;

        var message = job.Status switch
        {
            "failed" when !string.IsNullOrWhiteSpace(job.Message) => job.Message,
            "cancelled" => "The previous image request was interrupted. Your photographs are saved; generate the trophy image again.",
            "complete" => "Image generation finished but its saved result is unavailable. Generate the trophy image again.",
            _ => "Image generation was interrupted. Your photographs are saved; generate the trophy image again."
        };
        await store.SetIllustrationStatusAsync(trophy.Id, IllustrationStates.Failed, message, cancellationToken);
        return await store.GetTrophyAsync(trophy.Id, cancellationToken);
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
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(TimeSpan.FromMinutes(5));
        var stopped = cancellationToken;
        cancellationToken = requestCancellation.Token;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (activeGate) activeRequests[(job.ClubId, job.TrophyId)] = new(requestCancellation, finished);
        var started = false;
        try
        {
            var trophy = await store.GetTrophyAsync(job.TrophyId, cancellationToken);
            var references = await store.GetTrophyPhotoFilesAsync(job.TrophyId, cancellationToken);
            if (trophy is null || references.Count == 0 || !illustrator.IsAvailable)
            {
                var message = trophy is null
                    ? "The trophy no longer exists."
                    : references.Count == 0
                        ? "Add a trophy reference photograph first."
                        : "The illustration generator is not configured. Your photographs are saved.";
                billing.FailJob(job, message, false);
                if (trophy is not null)
                    await store.SetIllustrationStatusAsync(job.TrophyId, IllustrationStates.Failed, message, CancellationToken.None);
                return;
            }
            await store.SetIllustrationStatusAsync(job.TrophyId, IllustrationStates.Processing, "Creating the catalogue illustration from the saved photographs…", cancellationToken);
            started = billing.BeginProviderAttempt(job, trophy.TrophyPhotos.Count + trophy.Evidence.Count);
            if (!started) return;
            var image = await illustrator.GenerateAsync(trophy.Name, references, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await store.SaveIllustrationAsync(job.TrophyId, image, cancellationToken);
            billing.CompleteJob(job, "Catalogue illustration created.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Illustration job {JobId} stopped for club {ClubId}, trophy {TrophyId}", job.Id, job.ClubId, job.TrophyId);
            var message = started ? "Processing was interrupted. Your photos are saved. Please try again; no additional credit is needed." : exception is BillingException billingException ? billingException.Message : "This illustration could not start. Your photographs are safe.";
            billing.FailJob(job, message, started);
            try { await store.SetIllustrationStatusAsync(job.TrophyId, IllustrationStates.Failed, message, CancellationToken.None); } catch (Exception updateException) { logger.LogWarning(updateException, "Could not save illustration job status for {JobId}", job.Id); }
            if (stopped.IsCancellationRequested) throw;
        }
        finally {
            lock (activeGate) activeRequests.Remove((job.ClubId, job.TrophyId));
            finished.TrySetResult();
        }
    }
}
