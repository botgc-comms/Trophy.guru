using System.Security.Claims;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/public/integrations/intelligent-golf", async (HttpContext context, StripeBillingService stripe) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await stripe.IntegrationOfferAsync(context.RequestAborted));
        }).AllowAnonymous();

        app.MapGet("/api/billing", (HttpContext context, AccountStore accounts, BillingStore billing, StripeBillingService stripe) => Respond(async () =>
        {
            var account = await AccountAsync(context, accounts, billing, false);
            var balance = billing.Balance(account.ClubId!);
            var purchases = billing.Purchases(account.ClubId!);
            var upgrades = new List<BillingQuote>();
            foreach (var purchase in purchases.Where(x => x.State == "paid"))
                foreach (var pack in TrophyCreditPack.All)
                    try { upgrades.Add(billing.Quote(account.ClubId!, pack.Code, purchase.Id)); } catch (BillingException) { }
            var integrationOffer = await stripe.IntegrationOfferAsync(context.RequestAborted);
            return Results.Ok(new
            {
                balance = new { balance.Unlimited, balance.Available, balance.Reserved, balance.Used, balance.OnHold },
                clubId = account.ClubId, reviewJobs = billing.ReviewJobs(account.ClubId!), packs = TrophyCreditPack.All, upgrades,
                purchases = purchases.Select(x => new { x.Id, x.PackCode, x.Credits, x.AmountPence, x.State, x.UpgradeFrom, x.RequestId }),
                paymentsEnabled = stripe.Enabled, mode = stripe.Mode,
                owner = AccountSecurity.IsOwner(account), emailVerified = AccountSecurity.IsEmailVerified(account),
                portalAvailable = stripe.Enabled && balance.CustomerId != null,
                integrationAvailable = integrationOffer.Available, integrationOffer, integrationSubscription = billing.IntegrationSubscription(account.ClubId!),
                allowance = new { free = new { photos = 12, analyses = 3, illustrations = 2 }, paid = new { photos = 40, analyses = 12, illustrations = 3 } }
            });
        }));

        app.MapPost("/api/billing/checkout", (HttpContext context, BillingCheckoutInput input, AccountStore accounts, BillingStore billing, StripeBillingService stripe) => Respond(async () =>
        {
            var account = await AccountAsync(context, accounts, billing, true);
            return Results.Ok(new { url = await stripe.CheckoutAsync(account, input, context.RequestAborted) });
        }));
        app.MapPost("/api/billing/portal", (HttpContext context, AccountStore accounts, BillingStore billing, StripeBillingService stripe) => Respond(async () =>
        {
            var account = await AccountAsync(context, accounts, billing, true);
            return Results.Ok(new { url = await stripe.PortalAsync(account, context.RequestAborted) });
        }));
        app.MapPost("/api/billing/integration-checkout", (HttpContext context, IntegrationCheckoutInput input, AccountStore accounts, BillingStore billing, StripeBillingService stripe) => Respond(async () =>
        {
            var account = await AccountAsync(context, accounts, billing, true);
            return Results.Ok(new { url = await stripe.IntegrationCheckoutAsync(account, input.RequestId, context.RequestAborted) });
        }));
        app.MapPost("/api/billing/jobs/{jobId}/acknowledge", (HttpContext context, string jobId, JobReviewInput input, AccountStore accounts, BillingStore billing) => Respond(async () =>
        {
            var account = await AccountAsync(context, accounts, billing, true);
            if (!input.UnderstandAttemptStillCounts) throw new BillingException("acknowledgement_required", "Confirm that you checked the trophy and understand the interrupted attempt still counts.", 400);
            billing.AcknowledgeUnknownJob(account.ClubId!, jobId);
            return Results.Ok(new { acknowledged = true });
        }));

        app.MapPost("/api/billing/webhook", (HttpContext context, StripeBillingService stripe) => Respond(async () =>
        {
            const int limit = 1_048_576;
            if (context.Request.ContentLength > limit) return Results.StatusCode(413);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192]; int read;
            while ((read = await context.Request.Body.ReadAsync(bytes, context.RequestAborted)) > 0)
            {
                if (buffer.Length + read > limit) return Results.StatusCode(413);
                buffer.Write(bytes, 0, read);
            }
            await stripe.HandleWebhookAsync(buffer.ToArray(), context.Request.Headers["Stripe-Signature"].ToString(), context.RequestAborted);
            return Results.Ok(new { received = true });
        })).AllowAnonymous();
    }

    private static async Task<AccountRecord> AccountAsync(HttpContext context, AccountStore accounts, BillingStore billing, bool write)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = id == null ? null : await accounts.GetAccountAsync(id, context.RequestAborted);
        if (account?.ClubId == null) throw new BillingException("unauthorized", "Sign in and create your club first.", 401);
        if (write)
        {
            if (!AccountSecurity.IsOwner(account)) throw new BillingException("owner_required", "Only the club owner can manage purchases.", 403);
            if (!AccountSecurity.IsEmailVerified(account)) throw new BillingException("email_verification_required", "Verify your email before making a purchase.", 403);
            if (!RequestSecurity.IsSameOriginMutation(context.Request, context.RequestServices.GetRequiredService<IConfiguration>()))
                throw new BillingException("invalid_origin", "Refresh this page before changing billing.", 403);
        }
        billing.EnsureClub(account.ClubId, account.ClubId == "legacy" && account.HasUnlimitedTrophyCredits && accounts.LegacyArchiveExists);
        return account;
    }

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (BillingException e) { return Results.Json(new { error = e.Code, message = e.Message }, statusCode: e.StatusCode); }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid_json", message = "The payment request is invalid." }); }
        catch (HttpRequestException) { return Results.Json(new { error = "payment_network_error", message = "The payment provider could not be reached. Retry the same checkout." }, statusCode: 502); }
    }

    public sealed record IntegrationCheckoutInput(string RequestId);
    public sealed record JobReviewInput(bool UnderstandAttemptStillCounts);
}
