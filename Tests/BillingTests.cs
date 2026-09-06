using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class BillingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trophy-billing-test-" + Guid.NewGuid().ToString("N"));
    private readonly BillingStore store;
    public BillingTests() { Directory.CreateDirectory(root); store = new BillingStore(Path.Combine(root, "operations.sqlite")); store.InitializeAsync().GetAwaiter().GetResult(); store.EnsureClub("club-a"); store.EnsureClub("club-b"); }
    private DurableBillableJob Job(string trophy, string kind = "analysis", string club = "club-a") => store.ScheduleJob(club, trophy, kind, 1, DateTimeOffset.UtcNow);
    private BillingPurchase Buy(string code = "club", string? upgrade = null)
    {
        var purchase = store.CreatePurchase("club-a", new(code, Guid.NewGuid().ToString(), upgrade));
        store.FulfilPayment("evt-" + purchase.Id, purchase.Id, "cs-" + purchase.Id, "pi-" + purchase.Id, purchase.AmountPence, "gbp", "cus-a");
        return purchase;
    }
    [Fact] public async Task ConcurrentRequestsCannotSpendTheLastCreditTwice()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => { try { Job("trophy-" + i); return true; } catch (BillingException e) when (e.Code == "credits_required") { return false; } })));
        Assert.Single(results, result => result); Assert.Equal(0, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-a").Reserved);
    }
    [Fact] public void ReadingAndIllustrationShareOneTrophyCredit()
    {
        var first = Job("trophy"); Assert.Equal(first.Id, Job("trophy").Id); var illustration = Job("trophy", "illustration");
        Assert.Equal(1, store.Balance("club-a").Reserved);
        Assert.True(store.BeginProviderAttempt(first, 2)); Assert.False(store.BeginProviderAttempt(first, 2));
        Assert.True(store.BeginProviderAttempt(illustration, 2)); store.CompleteJob(first, "done"); store.CompleteJob(illustration, "done"); store.CompleteJob(first, "repeat");
        Assert.Equal(0, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-a").Used); Assert.Equal(0, store.Balance("club-a").Reserved);
    }
    [Fact] public void KnownFailureReleasesReservationWithoutErasingTheTrophy()
    {
        var job = Job("trophy"); store.FailJob(job, "No provider request sent", false);
        Assert.Equal(1, store.Balance("club-a").Available); Assert.Equal("failed", store.JobStatus("club-a", "trophy", "analysis")!.State);
    }
    [Fact] public async Task RestartResumesQueuedJobsButDoesNotReplayUnknownProviderOutcomes()
    {
        var running = Job("running"); store.BeginProviderAttempt(running, 1); var waiting = Job("waiting", club: "club-b");
        await store.InitializeAsync(); Assert.Equal("needs_review", store.JobStatus("club-a", "running", "analysis")!.State); Assert.Equal(waiting.Id, store.NextJob("analysis")!.Id);
        Assert.Throws<BillingException>(() => Job("running")); Assert.Throws<BillingException>(() => store.AcknowledgeUnknownJob("club-b", running.Id));
        store.AcknowledgeUnknownJob("club-a", running.Id); Assert.Equal(1, store.Balance("club-a").Available); Assert.NotEqual(running.Id, Job("running").Id);
    }
    [Fact] public void DuplicatePaymentEventsAndCheckoutRetriesCreditOnce()
    {
        var request = new BillingCheckoutInput("club", Guid.NewGuid().ToString()); var purchase = store.CreatePurchase("club-a", request);
        Assert.Equal(purchase.Id, store.CreatePurchase("club-a", request).Id);
        store.FulfilPayment("evt-one", purchase.Id, "cs-one", "pi-one", 6000, "gbp", "cus-a");
        store.FulfilPayment("evt-one", purchase.Id, "cs-one", "pi-one", 6000, "gbp", "cus-a");
        store.FulfilPayment("evt-two", purchase.Id, "cs-one", "pi-one", 6000, "gbp", "cus-a");
        Assert.Equal(11, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-b").Available);
    }
    [Fact] public void TenToFiftyUpgradeCostsDifferenceAndAddsForty()
    {
        var first = Buy(); var reading = Job("used"); store.BeginProviderAttempt(reading, 1); store.CompleteJob(reading, "done");
        var quote = store.Quote("club-a", "collection", first.Id); Assert.Equal(16500, quote.AmountPence); Assert.Equal(40, quote.Credits);
        Buy("collection", first.Id); Assert.Equal(50, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-a").Used);
        Assert.Throws<BillingException>(() => store.Quote("club-a", "complete", first.Id)); Assert.Throws<BillingException>(() => store.Quote("club-b", "collection", first.Id));
    }
    [Fact] public void RefundBeforePaymentDoesNotGrantSpendableCredits()
    {
        var purchase = store.CreatePurchase("club-a", new("club", Guid.NewGuid().ToString()));
        store.HoldPayment("evt-refund", "pi-refund", "charge.refunded", true);
        store.FulfilPayment("evt-paid", purchase.Id, "cs-refund", "pi-refund", 6000, "gbp", "cus-a");
        Assert.True(store.Balance("club-a").OnHold); Assert.Equal(1, store.Balance("club-a").Available); Assert.Throws<BillingException>(() => Job("blocked"));
    }
    [Fact] public void FullRefundRevokesCreditsOnceAndRetainsCompletedWork()
    {
        var purchase = Buy(); var job = Job("trophy"); store.BeginProviderAttempt(job, 1); store.CompleteJob(job, "done");
        store.HoldPayment("evt-refund", "pi-" + purchase.Id, "charge.refunded", true); store.HoldPayment("evt-refund-duplicate", "pi-" + purchase.Id, "charge.refunded", true);
        Assert.Equal(0, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-a").Used); Assert.Equal("complete", store.JobStatus("club-a", "trophy", "analysis")!.State);
    }
    [Fact] public void FullRefundAfterAnEarlierDisputeStillRevokesTheOriginalGrant()
    {
        var purchase = Buy(); store.HoldPayment("evt-dispute", "pi-" + purchase.Id, "charge.dispute.created", false);
        store.HoldPayment("evt-later-refund", "pi-" + purchase.Id, "charge.refunded", true);
        Assert.Equal(1, store.Balance("club-a").Available); Assert.True(store.Balance("club-a").OnHold);
    }
    [Fact] public void MismatchedPaymentDoesNotGrantCredits()
    {
        var purchase = store.CreatePurchase("club-a", new("club", Guid.NewGuid().ToString()));
        Assert.Throws<BillingException>(() => store.FulfilPayment("evt-bad", purchase.Id, "cs-one", "pi-one", 1, "gbp", "cus-a")); Assert.Equal(1, store.Balance("club-a").Available);
    }
    [Fact] public void FreeAllowanceIsFiniteAndLegacyAllowanceIsPreserved()
    {
        Assert.Throws<BillingException>(() => store.CheckPhotoAllowance("club-a", "trophy", 13));
        for (var i = 0; i < 3; i++) { var job = Job("trophy"); store.BeginProviderAttempt(job, 1); store.CompleteJob(job, "done"); }
        Assert.Throws<BillingException>(() => Job("trophy"));
        store.EnsureClub("legacy", true); for (var i = 0; i < 15; i++) Job("legacy-" + i, club: "legacy");
        Assert.True(store.Balance("legacy").Unlimited); store.CheckPhotoAllowance("legacy", "trophy", 500);
    }
    [Theory] [InlineData(0, true)] [InlineData(-301, false)] [InlineData(301, false)]
    public void WebhookSignatureRequiresAnAuthenticRecentRawBody(int age, bool expected)
    {
        var now = DateTimeOffset.UtcNow; var body = Encoding.UTF8.GetBytes("{\"event\":1}"); var stamp = now.AddSeconds(age).ToUnixTimeSeconds();
        var signature = Sign(body, stamp); Assert.Equal(expected, StripeBillingService.VerifySignature(body, signature, "whsec_fixture", now));
        Assert.False(StripeBillingService.VerifySignature(Encoding.UTF8.GetBytes("changed"), signature, "whsec_fixture", now));
    }
    [Fact] public async Task WebhookRetrievesCanonicalPaidSessionBeforeFulfilment()
    {
        var purchase = store.CreatePurchase("club-a", new("club", Guid.NewGuid().ToString())); store.SetCustomer("club-a", "cus-a");
        var session = JsonSerializer.Serialize(new { id = "cs-test", mode = "payment", status = "complete", payment_status = "paid", payment_intent = "pi-test", customer = "cus-a", amount_total = 6000, currency = "gbp", metadata = new { purchase_id = purchase.Id, club_id = "club-a" } });
        var handler = new FixtureHandler(session); var config = Config(); var stripe = new StripeBillingService(new FixtureClients(handler), config, store);
        var body = Encoding.UTF8.GetBytes("{\"id\":\"evt-test\",\"type\":\"checkout.session.completed\",\"livemode\":false,\"data\":{\"object\":{\"id\":\"cs-test\"}}}");
        await stripe.HandleWebhookAsync(body, Sign(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), default);
        Assert.Equal(11, store.Balance("club-a").Available); Assert.Equal(1, handler.Calls);
        await stripe.HandleWebhookAsync(body, Sign(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), default); Assert.Equal(11, store.Balance("club-a").Available);
    }
    [Fact] public void LivePaymentsRequireExplicitReadinessFlagsAndLiveKeys()
    {
        var config = Config(); config["BILLING_MODE"] = "live"; config["PUBLIC_SITE_URL"] = "https://archive.example";
        var stripe = new StripeBillingService(new FixtureClients(new FixtureHandler("{}")), config, store); Assert.False(stripe.Enabled);
        config["STRIPE_SECRET_KEY"] = "sk_live_fixture"; config["BILLING_LIVE_APPROVED"] = "true"; Assert.False(stripe.Enabled);
        config["BILLING_LEGAL_READY"] = "true"; Assert.True(stripe.Enabled); Assert.False(stripe.IntegrationAvailable);
    }
    private static IConfigurationRoot Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["BILLING_MODE"] = "test", ["STRIPE_SECRET_KEY"] = "sk_test_fixture", ["STRIPE_WEBHOOK_SECRET"] = "whsec_fixture", ["PUBLIC_SITE_URL"] = "http://127.0.0.1:5192" }).Build();
    private static string Sign(byte[] body, long stamp) => $"t={stamp},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_fixture"), Encoding.UTF8.GetBytes(stamp + ".").Concat(body).ToArray())).ToLowerInvariant()}";
    private sealed class FixtureHandler(string payload) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.stripe.com", request.RequestUri!.Host); Assert.Equal(HttpMethod.Get, request.Method); Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FixtureClients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
}
