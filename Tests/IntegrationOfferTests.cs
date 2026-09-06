using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class IntegrationOfferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trophy-integration-test-" + Guid.NewGuid().ToString("N"));
    private readonly BillingStore store;
    private readonly IConfigurationRoot configuration;
    private readonly FixtureHandler handler = new();
    private readonly AccountRecord account = new() { Id = "fixture-owner", DisplayName = "Fixture owner", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", ClubId = "fixture-club" };

    public IntegrationOfferTests()
    {
        Directory.CreateDirectory(root);
        store = new BillingStore(Path.Combine(root, "operations.sqlite"));
        store.InitializeAsync().GetAwaiter().GetResult();
        store.EnsureClub("fixture-club"); store.SetCustomer("fixture-club", "cus_fixture");
        configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BILLING_MODE"] = "test", ["STRIPE_SECRET_KEY"] = "sk_test_fixture", ["STRIPE_WEBHOOK_SECRET"] = "whsec_fixture",
            ["PUBLIC_SITE_URL"] = "http://127.0.0.1:5192", ["IG_INTEGRATION_AVAILABLE"] = "true", ["STRIPE_IG_PRICE_ID"] = "price_fixture"
        }).Build();
    }
    private StripeBillingService Service() => new(new FixtureClients(handler), configuration, store);

    [Fact]
    public async Task DisabledIntegrationAdvertisesChosenAnnualPriceWithoutContactingStripe()
    {
        configuration["IG_INTEGRATION_AVAILABLE"] = "false";
        var service = Service(); var offer = await service.IntegrationOfferAsync(default);
        Assert.Equal(29900, offer.AmountPence); Assert.Equal("gbp", offer.Currency); Assert.Equal("year", offer.BillingInterval);
        Assert.False(offer.Available); Assert.Equal("coming_soon", offer.Status); Assert.Empty(handler.Paths);
        var error = await Assert.ThrowsAsync<BillingException>(() => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal("integration_unavailable", error.Code); Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task AnnualPriceMustBeVerifiedAndMetadataUsesTheSharedOfferContract()
    {
        var service = Service(); Assert.False(service.IntegrationAvailable);
        var offer = await service.IntegrationOfferAsync(default);
        Assert.True(offer.Available); Assert.True(service.IntegrationAvailable); Assert.Null(offer.AvailabilityReason);
        var json = JsonSerializer.SerializeToElement(offer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("intelligent-golf", json.GetProperty("code").GetString()); Assert.Equal(29900, json.GetProperty("amountPence").GetInt64());
        Assert.Equal("year", json.GetProperty("billingInterval").GetString());
        Assert.Equal(offer, await service.IntegrationOfferAsync(default)); Assert.Single(handler.Paths);
    }

    [Theory]
    [InlineData("month")]
    [InlineData("two-years")]
    [InlineData("wrong-amount")]
    [InlineData("wrong-currency")]
    [InlineData("live")]
    [InlineData("inactive")]
    [InlineData("one-time")]
    [InlineData("tiered")]
    [InlineData("custom-amount")]
    [InlineData("quantity-transform")]
    [InlineData("metered")]
    [InlineData("missing-amount")]
    [InlineData("wrong-id")]
    [InlineData("malformed")]
    public async Task UnsupportedPricesCannotCreateSubscriptionCheckouts(string invalid)
    {
        var price = JsonNode.Parse(handler.Price)!.AsObject();
        switch (invalid)
        {
            case "month": price["recurring"]!["interval"] = "month"; break;
            case "two-years": price["recurring"]!["interval_count"] = 2; break;
            case "wrong-amount": price["unit_amount"] = 29899; break;
            case "wrong-currency": price["currency"] = "usd"; break;
            case "live": price["livemode"] = true; break;
            case "inactive": price["active"] = false; break;
            case "one-time": price["type"] = "one_time"; break;
            case "tiered": price["billing_scheme"] = "tiered"; price["tiers_mode"] = "graduated"; break;
            case "custom-amount": price["custom_unit_amount"] = new JsonObject { ["enabled"] = true }; break;
            case "quantity-transform": price["transform_quantity"] = new JsonObject { ["divide_by"] = 10, ["round"] = "up" }; break;
            case "metered": price["recurring"]!["usage_type"] = "metered"; break;
            case "missing-amount": price.Remove("unit_amount"); break;
            case "wrong-id": price["id"] = "price_other"; break;
            case "malformed": price["livemode"] = "false"; break;
        }
        handler.Price = price.ToJsonString(); var service = Service();
        var offer = await service.IntegrationOfferAsync(default); Assert.False(offer.Available); Assert.Equal(29900, offer.AmountPence);
        Assert.Equal("price_unavailable", offer.Status);
        var error = await Assert.ThrowsAsync<BillingException>(() => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal("integration_unavailable", error.Code); Assert.Empty(handler.CheckoutKeys);
    }

    [Fact]
    public async Task ProviderFailureDoesNotBreakReadingPricesOrEnablePurchases()
    {
        handler.PriceNetworkFailure = true;
        var offer = await Service().IntegrationOfferAsync(default);
        Assert.False(offer.Available); Assert.Equal(29900, offer.AmountPence); Assert.Equal("price_unavailable", offer.Status);
        Assert.Empty(handler.CheckoutKeys);
    }

    [Fact]
    public async Task MissingPaymentConfigurationDoesNotContactStripe()
    {
        configuration["BILLING_MODE"] = "disabled";
        var offer = await Service().IntegrationOfferAsync(default);
        Assert.False(offer.Available); Assert.Equal("payments_unavailable", offer.Status); Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task CheckoutRevalidatesAnAnnualPriceEvenWhenPublicMetadataWasCached()
    {
        var service = Service(); Assert.True((await service.IntegrationOfferAsync(default)).Available);
        handler.Price = handler.Price.Replace("\"year\"", "\"month\"");
        await Assert.ThrowsAsync<BillingException>(() => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Empty(handler.CheckoutKeys);
    }

    [Fact]
    public async Task FreshRequestIdsAndRestartReuseOneStoredSubscriptionCheckout()
    {
        var service = Service();
        var urls = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default)));
        Assert.Single(urls.Distinct()); Assert.Single(handler.CheckoutKeys);
        var restarted = new BillingStore(Path.Combine(root, "operations.sqlite")); await restarted.InitializeAsync();
        var afterRestart = new StripeBillingService(new FixtureClients(handler), configuration, restarted);
        Assert.Equal(urls[0], await afterRestart.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Single(handler.CheckoutKeys);
    }

    [Fact]
    public async Task LostProviderResponseRetriesTheSameDurableIdempotencyKey()
    {
        handler.FailNextCheckout = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Service().IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        await Service().IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        Assert.Equal(2, handler.CheckoutKeys.Count); Assert.Single(handler.CheckoutKeys.Distinct());
    }

    [Fact]
    public async Task OldUnknownCheckoutCannotBecomeANewChargeAfterProviderIdempotencyExpires()
    {
        handler.FailNextCheckout = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Service().IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(root, "operations.sqlite")))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "UPDATE integration_checkouts SET created_at=$old";
            command.Parameters.AddWithValue("$old", DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds()); command.ExecuteNonQuery();
        }
        var error = await Assert.ThrowsAsync<BillingException>(() => Service().IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal("integration_checkout_review", error.Code); Assert.Single(handler.CheckoutKeys);
    }

    [Fact]
    public async Task CanonicallyExpiredSessionAllowsOneReplacementCheckout()
    {
        var service = Service(); await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        handler.SessionStatus = "expired";
        await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        Assert.Equal(2, handler.CheckoutKeys.Distinct().Count());
    }

    [Theory]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("paused")]
    [InlineData("trialing")]
    public async Task CompletedUnpaidSubscriptionMustBeManagedInsteadOfPurchasedAgain(string subscriptionStatus)
    {
        var service = Service(); await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        handler.SessionStatus = "complete"; handler.SubscriptionStatus = subscriptionStatus;
        var error = await Assert.ThrowsAsync<BillingException>(() => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal("already_subscribed", error.Code); Assert.Single(handler.CheckoutKeys);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("incomplete_expired")]
    public async Task TerminalSubscriptionAllowsExactlyOneReplacementCheckout(string subscriptionStatus)
    {
        var service = Service(); await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        handler.SessionStatus = "complete"; handler.SubscriptionStatus = subscriptionStatus;
        var replacement = await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        Assert.Equal(replacement, await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal(2, handler.CheckoutKeys.Distinct().Count());
    }

    [Fact]
    public void SubscriptionMetadataSeparatesPaidBillingStateFromInstallationAndOtherClubs()
    {
        var until = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();
        Assert.Equal(new IntegrationSubscriptionState(false, false, "none", null), store.IntegrationSubscription("fixture-club"));
        store.SyncSubscription("sub_fixture", "fixture-club", "active", until, "price_fixture", "active");
        var paid = store.IntegrationSubscription("fixture-club");
        Assert.True(paid.Exists); Assert.True(paid.Current); Assert.Equal("active", paid.Status); Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(until), paid.PaidThrough);
        Assert.False(store.IntegrationSubscription("another-club").Exists);
        store.SyncSubscription("sub_fixture", "fixture-club", "inactive", until, "price_fixture", "past_due");
        var unpaid = store.IntegrationSubscription("fixture-club"); Assert.True(unpaid.Current); Assert.Equal("past_due", unpaid.Status); Assert.Null(unpaid.PaidThrough);
        store.SyncSubscription("sub_fixture", "fixture-club", "inactive", until, "price_fixture", "canceled");
        var canceled = store.IntegrationSubscription("fixture-club"); Assert.True(canceled.Exists); Assert.False(canceled.Current); Assert.Null(canceled.PaidThrough);
    }

    [Fact]
    public async Task SidecarStatusMigrationKeepsExistingSubscriptionRowsAndCreditBalance()
    {
        var until = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();
        store.SyncSubscription("sub_fixture", "fixture-club", "active", until, "price_fixture");
        var balance = store.Balance("fixture-club");
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(root, "operations.sqlite")))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "ALTER TABLE integration_subscriptions DROP COLUMN stripe_status"; command.ExecuteNonQuery();
        }
        await store.InitializeAsync();
        Assert.Equal(balance, store.Balance("fixture-club")); Assert.True(store.IntegrationSubscription("fixture-club").Current);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(until), store.IntegrationSubscription("fixture-club").PaidThrough);
    }

    [Fact]
    public async Task FinancialHoldBlocksReusingAnExistingIntegrationCheckout()
    {
        var service = Service(); await service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default);
        var purchase = store.CreatePurchase("fixture-club", new("club", Guid.NewGuid().ToString()));
        store.FulfilPayment("evt_fixture_paid", purchase.Id, "cs_credit_fixture", "pi_fixture", purchase.AmountPence, "gbp", "cus_fixture");
        store.HoldPayment("evt_fixture_hold", "pi_fixture", "charge.dispute.created", false);
        var lookups = handler.Paths.Count(path => path.Contains("/checkout/sessions/"));
        var error = await Assert.ThrowsAsync<BillingException>(() => service.IntegrationCheckoutAsync(account, Guid.NewGuid().ToString(), default));
        Assert.Equal("billing_review", error.Code); Assert.Single(handler.CheckoutKeys);
        Assert.Equal(lookups, handler.Paths.Count(path => path.Contains("/checkout/sessions/")));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public string Price { get; set; } = """{"id":"price_fixture","object":"price","active":true,"livemode":false,"currency":"gbp","type":"recurring","billing_scheme":"per_unit","unit_amount":29900,"custom_unit_amount":null,"tiers_mode":null,"transform_quantity":null,"recurring":{"interval":"year","interval_count":1,"usage_type":"licensed"}}""";
        public bool PriceNetworkFailure { get; set; }
        public bool FailNextCheckout { get; set; }
        public string SessionStatus { get; set; } = "open";
        public string SubscriptionStatus { get; set; } = "active";
        public List<string> Paths { get; } = [];
        public List<string> CheckoutKeys { get; } = [];
        private string? orderId;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.stripe.com", request.RequestUri!.Host); Paths.Add(request.RequestUri.AbsolutePath);
            string payload;
            if (request.RequestUri.AbsolutePath.Contains("/prices/"))
            {
                if (PriceNetworkFailure) throw new HttpRequestException("Fixture provider offline");
                payload = Price;
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/checkout/sessions") && request.Method == HttpMethod.Post)
            {
                var values = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
                Assert.Equal("subscription", values["mode"]); Assert.Equal("price_fixture", values["line_items[0][price]"]); Assert.Equal("1", values["line_items[0][quantity]"]);
                orderId = values["metadata[integration_order_id]"]; CheckoutKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                if (FailNextCheckout) { FailNextCheckout = false; throw new HttpRequestException("Fixture lost response"); }
                SessionStatus = "open";
                payload = JsonSerializer.Serialize(new { id = "cs_" + orderId, url = "https://checkout.stripe.com/c/pay/cs_" + orderId });
            }
            else if (request.RequestUri.AbsolutePath.Contains("/checkout/sessions/cs_"))
                payload = JsonSerializer.Serialize(new { id = "cs_" + orderId, mode = "subscription", livemode = false, customer = "cus_fixture", status = SessionStatus, subscription = SessionStatus == "complete" ? "sub_fixture" : null, url = "https://checkout.stripe.com/c/pay/cs_" + orderId, metadata = new { club_id = "fixture-club", integration_order_id = orderId } });
            else if (request.RequestUri.AbsolutePath.EndsWith("/subscriptions/sub_fixture"))
                payload = JsonSerializer.Serialize(new { id = "sub_fixture", customer = "cus_fixture", status = SubscriptionStatus, current_period_end = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(), latest_invoice = new { status = "open" }, metadata = new { club_id = "fixture-club" }, items = new { data = new[] { new { price = "price_fixture" } } } });
            else throw new InvalidOperationException("Unexpected fixture request: " + request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FixtureClients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("trophy-integration-test-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup path");
        Directory.Delete(resolved, recursive: true);
    }
}
