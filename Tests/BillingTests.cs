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
    [Fact] public void KnownFailureKeepsCreditAssignedToTheSameTrophy()
    {
        var job = Job("trophy"); store.FailJob(job, "No provider request sent", false);
        Assert.Equal(0, store.Balance("club-a").Available); Assert.Equal(1, store.Balance("club-a").Reserved); Assert.Equal("failed", store.JobStatus("club-a", "trophy", "analysis")!.State);
    }
    [Fact] public async Task RestartResumesQueuedJobsButDoesNotReplayUnknownProviderOutcomes()
    {
        var running = Job("running"); store.BeginProviderAttempt(running, 1); var waiting = Job("waiting", club: "club-b");
        await store.InitializeAsync(); Assert.Equal("failed", store.JobStatus("club-a", "running", "analysis")!.State); Assert.Equal(waiting.Id, store.NextJob("analysis")!.Id);
        Assert.Empty(store.ReviewJobs("club-a"));
        Assert.Equal(0, store.Balance("club-a").Available); Assert.NotEqual(running.Id, Job("running").Id);
    }
    [Fact] public async Task ExistingInterruptedJobsAreMigratedAndCreditIdentitySurvivesRetries()
    {
        var job = Job("trophy"); store.BeginProviderAttempt(job, 1);
        using var db = new SqliteConnection($"Data Source={Path.Combine(root, "operations.sqlite")}"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT credit_id FROM trophy_allocations WHERE club_id='club-a' AND trophy_id='trophy'";
        var creditId = Assert.IsType<string>(command.ExecuteScalar()); Assert.NotEmpty(creditId);
        command.CommandText = "UPDATE billable_jobs SET state='needs_review'"; command.ExecuteNonQuery();
        await store.InitializeAsync();
        Assert.Empty(store.ReviewJobs("club-a"));
        var retry = Job("trophy"); Assert.True(store.BeginProviderAttempt(retry, 1));
        store.FailJob(retry, "Interrupted", true);
        var next = Job("trophy"); Assert.True(store.BeginProviderAttempt(next, 1)); store.CompleteJob(next, "done");
        command.CommandText = "SELECT credit_id FROM trophy_allocations WHERE club_id='club-a' AND trophy_id='trophy'";
        Assert.Equal(creditId, command.ExecuteScalar()); Assert.Equal(1, store.Balance("club-a").Used);
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
    [Fact] public void TenToFiftyUpgradeChargesFortyCreditsAtTheFiftyPackRate()
    {
        var first = Buy(); var reading = Job("used"); store.BeginProviderAttempt(reading, 1); store.CompleteJob(reading, "done");
        var quote = store.Quote("club-a", "collection", first.Id); Assert.Equal(14000, quote.AmountPence); Assert.Equal(40, quote.Credits);
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
    [Fact] public void AllFutureReadingsAndIllustrationsUseTheSameCredit()
    {
        Assert.Throws<BillingException>(() => store.CheckPhotoAllowance("club-a", "trophy", 13));
        for (var i = 0; i < 25; i++) { foreach (var kind in new[] { "analysis", "illustration" }) { var job = Job("trophy", kind); Assert.True(store.BeginProviderAttempt(job, 1)); store.CompleteJob(job, "done"); } }
        Assert.Equal(1, store.Balance("club-a").Used); Assert.Equal(0, store.Balance("club-a").Available);
        Assert.Throws<BillingException>(() => Job("another-trophy"));
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
    [Theory]
    [InlineData(150, 37500)]
    [InlineData(250, 62500)]
    [InlineData(251, 62750)]
    [InlineData(300, 75000)]
    [InlineData(500, 125000)]
    public void VolumeOrdersChargeTwoFiftyAndCreditExactQuantity(int credits, long amount)
    {
        var input = new BillingCheckoutInput("complete", Guid.NewGuid().ToString(), Credits: credits);
        var purchase = store.CreatePurchase("club-a", input);
        Assert.Equal(amount, purchase.AmountPence); Assert.Equal(credits, purchase.Credits);
        Assert.Equal(purchase.Id, store.CreatePurchase("club-a", input).Id);
        Assert.Equal("request_conflict", Assert.Throws<BillingException>(() => store.CreatePurchase("club-a", input with { Credits = credits + 1 })).Code);
        store.FulfilPayment("evt-volume", purchase.Id, "cs-volume", "pi-volume", amount, "gbp", "cus-a");
        Assert.Equal(credits + 1, store.Balance("club-a").Available);
    }
    [Theory]
    [InlineData("complete", 149)]
    [InlineData("complete", 0)]
    [InlineData("complete", -1)]
    [InlineData("club", 300)]
    public void InvalidVolumeQuantitiesCannotCreateAnOrder(string code, int credits)
    {
        Assert.Equal("invalid_quantity", Assert.Throws<BillingException>(() => store.CreatePurchase("club-a", new(code, Guid.NewGuid().ToString(), Credits: credits))).Code);
        Assert.Empty(store.Purchases("club-a"));
    }
    [Fact] public void HistoricalPricesDoNotAffectCreditBasedUpgrades()
    {
        var old = Buy("collection");
        using var db = new SqliteConnection($"Data Source={Path.Combine(root, "operations.sqlite")}"); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE billing_purchases SET amount_pence=22500 WHERE id=$id"; cmd.Parameters.AddWithValue("$id", old.Id); cmd.ExecuteNonQuery();
        var quote = store.Quote("club-a", "complete", old.Id);
        Assert.Equal(25000, quote.AmountPence); Assert.Equal(100, quote.Credits);
        var first = Buy("club"); var upgrade = Buy("collection", first.Id);
        var next = store.Quote("club-a", "complete", upgrade.Id);
        Assert.Equal(25000, next.AmountPence); Assert.Equal(100, next.Credits);
    }
    [Fact] public void FourPaidCreditsUpgradeByQuantityAtTargetRateAndCannotBeReusedConcurrently()
    {
        for (var i = 0; i < 4; i++) Buy("single");
        var basis = store.UpgradeBasis("club-a"); Assert.Equal(4, basis.Credits);
        var ten = store.Quote("club-a", "club", basis.UpgradeFrom); Assert.Equal(6, ten.Credits); Assert.Equal(3600, ten.AmountPence);
        var fifty = store.Quote("club-a", "collection", basis.UpgradeFrom); Assert.Equal(46, fifty.Credits); Assert.Equal(16100, fifty.AmountPence);
        var bulk = store.Quote("club-a", "complete", basis.UpgradeFrom); Assert.Equal(146, bulk.Credits); Assert.Equal(36500, bulk.AmountPence);
        Assert.Throws<BillingException>(() => store.Quote("club-b", "club", basis.UpgradeFrom));
        var input = new BillingCheckoutInput("club", Guid.NewGuid().ToString(), basis.UpgradeFrom);
        var purchase = store.CreatePurchase("club-a", input);
        Assert.Equal(purchase.Id, store.CreatePurchase("club-a", input).Id);
        Assert.Throws<BillingException>(() => store.CreatePurchase("club-a", new("collection", Guid.NewGuid().ToString(), basis.UpgradeFrom)));
        store.FulfilPayment("upgrade-four", purchase.Id, "cs-four", "pi-four", 3600, "gbp", "cus-a");
        Assert.Equal(10, store.UpgradeBasis("club-a").Credits); Assert.Equal(11, store.Balance("club-a").Available);
        Assert.Throws<BillingException>(() => store.Quote("club-a", "collection", basis.UpgradeFrom));
        Assert.Equal(14000, store.Quote("club-a", "collection", store.UpgradeBasis("club-a").UpgradeFrom).AmountPence);
    }
    [Fact] public void CabinetDefaultAndUpgradeUseTheNewPrice()
    {
        Assert.Equal(37500, store.Quote("club-a", "complete", null).AmountPence);
        var previous = Buy("collection");
        var quote = store.Quote("club-a", "complete", previous.Id);
        Assert.Equal(25000, quote.AmountPence); Assert.Equal(100, quote.Credits);
    }
    [Theory]
    [InlineData(150, 37500)]
    [InlineData(250, 62500)]
    [InlineData(300, 75000)]
    [InlineData(500, 125000)]
    public async Task LiveCheckoutSendsServerPriceAndOnlyWebhookGrantsCredits(int credits, long amount)
    {
        var config = Config(); config["BILLING_MODE"] = "live"; config["STRIPE_SECRET_KEY"] = "sk_live_fixture";
        config["PUBLIC_SITE_URL"] = "https://trophy.guru"; config["BILLING_LIVE_APPROVED"] = "true"; config["BILLING_LEGAL_READY"] = "true";
        store.SetCustomer("club-a", "cus-live-fixture");
        var handler = new CheckoutFixtureHandler();
        var stripe = new StripeBillingService(new FixtureClients(handler), config, store);
        var account = new AccountRecord { Id = "owner-a", ClubId = "club-a", Email = "owner@example.test", DisplayName = "Fixture owner", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var input = new BillingCheckoutInput("complete", Guid.NewGuid().ToString(), Credits: credits);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_live_fixture", await stripe.CheckoutAsync(account, input, default));
        Assert.Equal(amount.ToString(), handler.Fields["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("gbp", handler.Fields["line_items[0][price_data][currency]"]);
        Assert.Equal("inclusive", handler.Fields["line_items[0][price_data][tax_behavior]"]);
        Assert.Equal("1", handler.Fields["line_items[0][quantity]"]);
        Assert.Contains(credits.ToString(), handler.Fields["line_items[0][price_data][product_data][name]"]);
        Assert.StartsWith("https://trophy.guru/archive.html?billing=success&purchase=", handler.Fields["success_url"]);
        Assert.EndsWith("#catalogue", handler.Fields["success_url"]);
        Assert.Equal(1, store.Balance("club-a").Available);
        await stripe.CheckoutAsync(account, input, default); Assert.Equal(1, handler.Posts);
        var purchase = Assert.Single(store.Purchases("club-a"));
        handler.Payment = JsonSerializer.Serialize(new { id = "cs_live_fixture", mode = "payment", status = "complete", payment_status = "paid", payment_intent = "pi-live-fixture", customer = "cus-live-fixture", amount_total = amount, currency = "gbp", metadata = new { purchase_id = purchase.Id, club_id = "club-a" } });
        var body = Encoding.UTF8.GetBytes("{\"id\":\"evt-live-fixture\",\"type\":\"checkout.session.completed\",\"livemode\":true,\"data\":{\"object\":{\"id\":\"cs_live_fixture\"}}}");
        await stripe.HandleWebhookAsync(body, Sign(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), default);
        await stripe.HandleWebhookAsync(body, Sign(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), default);
        Assert.Equal(credits + 1, store.Balance("club-a").Available);
    }
    [Theory]
    [InlineData(401, "invalid_api_key", "authenticate")]
    [InlineData(400, "resource_missing", "test/live")]
    [InlineData(400, "idempotency_error", "reuse")]
    public async Task StripeFailuresExposeSafeRequestReferenceWithoutProviderSecrets(int status, string code, string expected)
    {
        store.SetCustomer("club-a", "cus-fixture");
        var handler = new RejectedCheckoutHandler(status, code);
        var stripe = new StripeBillingService(new FixtureClients(handler), Config(), store);
        var account = new AccountRecord { Id = "owner", ClubId = "club-a", Email = "owner@example.test", DisplayName = "Owner", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var error = await Assert.ThrowsAsync<BillingException>(() => stripe.CheckoutAsync(account, new("single", Guid.NewGuid().ToString()), default));
        Assert.Contains(expected, error.Message); Assert.Contains("req_fixture123", error.Message);
        Assert.DoesNotContain("private-provider-data", error.Message);
        Assert.Equal(1, store.Balance("club-a").Available);
    }
    private sealed class RejectedCheckoutHandler(int status, string code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { code, message = "private-provider-data" } })) };
            response.Headers.Add("Request-Id", "req_fixture123"); return Task.FromResult(response);
        }
    }
    [Fact]
    public async Task ManagedPaymentsRejectionUsesStandardCheckoutAndReusesSuccessfulSession()
    {
        store.SetCustomer("club-a", "cus-fixture");
        var handler = new ManagedPaymentsConflictHandler();
        var stripe = new StripeBillingService(new FixtureClients(handler), Config(), store);
        var account = new AccountRecord { Id = "owner", ClubId = "club-a", Email = "owner@example.test", DisplayName = "Owner", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var input = new BillingCheckoutInput("single", Guid.NewGuid().ToString());
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_fixed", await stripe.CheckoutAsync(account, input, default));
        Assert.Equal(2, handler.Keys.Count); Assert.StartsWith("checkout:", handler.Keys[0]); Assert.StartsWith("checkout:standard-v1:", handler.Keys[1]);
        Assert.Equal("false", handler.Fields["managed_payments[enabled]"]);
        Assert.Equal("inclusive", handler.Fields["line_items[0][price_data][tax_behavior]"]);
        Assert.Equal("750", handler.Fields["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_fixed", await stripe.CheckoutAsync(account, input, default));
        Assert.Equal(2, handler.Keys.Count); Assert.Equal(1, store.Balance("club-a").Available);
    }
    private sealed class ManagedPaymentsConflictHandler : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];
        public Dictionary<string, string> Fields { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Fields = body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace("+", " ")));
            if (Keys.Count == 1) return new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { type = "invalid_request_error", message = "Unsupported parameter: payment_method_types. Managed Payments handles this parameter. Pass managed_payments[enabled]=false to disable it for this request." } })) };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"cs_fixed\",\"url\":\"https://checkout.stripe.com/c/pay/cs_fixed\"}") };
        }
    }
    private sealed class CheckoutFixtureHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Fields { get; private set; } = [];
        public string Payment { get; set; } = "{}";
        public int Posts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.stripe.com", request.RequestUri!.Host);
            Assert.Equal("sk_live_fixture", request.Headers.Authorization!.Parameter);
            string result;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/v1/checkout/sessions", request.RequestUri.AbsolutePath); Posts++;
                Fields = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]).Replace('+', ' '));
                result = "{\"id\":\"cs_live_fixture\",\"url\":\"https://checkout.stripe.com/c/pay/cs_live_fixture\"}";
            }
            else { Assert.Equal(HttpMethod.Get, request.Method); result = Payment; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result, Encoding.UTF8, "application/json") };
        }
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
