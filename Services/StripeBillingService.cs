using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

/// <summary>Hosted Stripe checkout; no card details are accepted by this application.</summary>
public sealed class StripeBillingService(IHttpClientFactory clients, IConfiguration configuration, BillingStore billing)
{
    private readonly SemaphoreSlim webhookGate = new(1, 1);
    private readonly SemaphoreSlim offerGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> integrationCheckoutGates = new(StringComparer.Ordinal);
    private IntegrationOffer? cachedIntegrationOffer;
    private string? cachedIntegrationPrice;
    private DateTimeOffset integrationOfferExpires;

    public string Mode => configuration["BILLING_MODE"] ?? "disabled";
    private string SecretKey => configuration["STRIPE_SECRET_KEY"] ?? "";
    private string WebhookSecret => configuration["STRIPE_WEBHOOK_SECRET"] ?? "";
    private string IntegrationPrice => configuration["STRIPE_IG_PRICE_ID"] ?? "";
    private bool IntegrationConfigured => Enabled && configuration.GetValue<bool>("IG_INTEGRATION_AVAILABLE") && IntegrationPrice.StartsWith("price_", StringComparison.Ordinal);
    public bool IntegrationAvailable => IntegrationConfigured && cachedIntegrationPrice == Mode + ":" + IntegrationPrice && cachedIntegrationOffer?.Available == true && integrationOfferExpires > DateTimeOffset.UtcNow;
    public bool Enabled => ValidConfiguration();

    private bool ValidConfiguration()
    {
        if (!WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal)) return false;
        if (!Uri.TryCreate(configuration["PUBLIC_SITE_URL"], UriKind.Absolute, out var site) || site.AbsolutePath != "/" || !string.IsNullOrEmpty(site.Query) || !string.IsNullOrEmpty(site.Fragment)) return false;
        return Mode switch
        {
            "test" => SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) && (site.Scheme == "https" || (site.Scheme == "http" && site.IsLoopback)),
            "live" => SecretKey.StartsWith("sk_live_", StringComparison.Ordinal) && site.Scheme == "https" && configuration.GetValue<bool>("BILLING_LIVE_APPROVED") && configuration.GetValue<bool>("BILLING_LEGAL_READY"),
            _ => false
        };
    }

    public async Task<IntegrationOffer> IntegrationOfferAsync(CancellationToken cancellationToken, bool refresh = false)
    {
        if (!configuration.GetValue<bool>("IG_INTEGRATION_AVAILABLE"))
            return IntegrationOffer.IntelligentGolf(false, "coming_soon", "Planned optional extra. Purchases open after the supported Intelligent Golf integration is ready to deliver.");
        if (!Enabled)
            return IntegrationOffer.IntelligentGolf(false, "payments_unavailable", "Online payments are not enabled yet.");
        if (!IntegrationConfigured)
            return IntegrationOffer.IntelligentGolf(false, "price_unavailable", "The annual integration checkout is not configured yet.");
        await offerGate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && cachedIntegrationPrice == Mode + ":" + IntegrationPrice && cachedIntegrationOffer != null && integrationOfferExpires > DateTimeOffset.UtcNow)
                return cachedIntegrationOffer;
            IntegrationOffer offer;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var result = await RequestAsync(HttpMethod.Get, "prices/" + Uri.EscapeDataString(IntegrationPrice), null, null, timeout.Token);
                offer = IsSupportedIntegrationPrice(result.RootElement)
                    ? IntegrationOffer.IntelligentGolf(true, "available")
                    : IntegrationOffer.IntelligentGolf(false, "price_unavailable", "The annual integration checkout is not configured with the advertised £299 GBP yearly price.");
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or BillingException || e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                offer = IntegrationOffer.IntelligentGolf(false, "price_unavailable", "The annual checkout could not be verified. Please try again later.");
            }
            cachedIntegrationOffer = offer;
            cachedIntegrationPrice = Mode + ":" + IntegrationPrice;
            integrationOfferExpires = DateTimeOffset.UtcNow.AddSeconds(60);
            return offer;
        }
        finally { offerGate.Release(); }
    }

    private bool IsSupportedIntegrationPrice(JsonElement price)
    {
        if (price.ValueKind != JsonValueKind.Object || String(price, "id") != IntegrationPrice || String(price, "object") != "price" ||
            !True(price, "active") || !price.TryGetProperty("livemode", out var live) || live.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || live.GetBoolean() != (Mode == "live") ||
            String(price, "currency") != "gbp" || String(price, "type") != "recurring" || String(price, "billing_scheme") != "per_unit" ||
            !NullOrMissing(price, "custom_unit_amount") || !NullOrMissing(price, "tiers_mode") || !NullOrMissing(price, "transform_quantity") ||
            !price.TryGetProperty("unit_amount", out var amount) || amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt64(out var pence) || pence != IntegrationOffer.IntelligentGolfAmountPence ||
            !price.TryGetProperty("recurring", out var recurring) || recurring.ValueKind != JsonValueKind.Object ||
            String(recurring, "interval") != "year" || String(recurring, "usage_type") != "licensed" ||
            !recurring.TryGetProperty("interval_count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var periods) || periods != 1)
            return false;
        return true;
    }
    private static bool True(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    private static bool NullOrMissing(JsonElement value, string key) => !value.TryGetProperty(key, out var property) || property.ValueKind == JsonValueKind.Null;

    private string SiteUrl => configuration["PUBLIC_SITE_URL"]!.TrimEnd('/');
    private void RequireEnabled() { if (!Enabled) throw new BillingException("payments_unavailable", "Payments are not enabled. Your existing archive remains available.", 503); }

    public async Task<string> CheckoutAsync(AccountRecord account, BillingCheckoutInput input, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var purchase = billing.CreatePurchase(account.ClubId!, input);
        if (purchase.State != "pending") throw new BillingException("checkout_complete", "This checkout has already completed or expired. Refresh your balance.");
        if (purchase.CheckoutUrl is not null) return purchase.CheckoutUrl;
        var customer = await CustomerAsync(account, cancellationToken);
        var values = new Dictionary<string, string>
        {
            ["mode"] = "payment", ["customer"] = customer,
            ["success_url"] = SiteUrl + "/archive.html?billing=success", ["cancel_url"] = SiteUrl + "/archive.html?billing=cancelled",
            ["client_reference_id"] = purchase.Id, ["metadata[purchase_id]"] = purchase.Id, ["metadata[club_id]"] = purchase.ClubId,
            ["payment_intent_data[metadata][purchase_id]"] = purchase.Id, ["payment_intent_data[metadata][club_id]"] = purchase.ClubId,
            ["line_items[0][price_data][currency]"] = "gbp", ["line_items[0][price_data][unit_amount]"] = purchase.AmountPence.ToString(CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"] = $"Trophy Archive — {purchase.Credits} trophy credits" + (purchase.UpgradeFrom != null ? " (pack upgrade)" : ""),
            ["line_items[0][quantity]"] = "1", ["payment_method_types[0]"] = "card"
        };
        using var result = await RequestAsync(HttpMethod.Post, "checkout/sessions", values, "checkout:" + purchase.Id, cancellationToken);
        var url = RequiredString(result.RootElement, "url");
        ValidateStripeRedirect(url, "checkout.stripe.com");
        billing.AttachCheckout(purchase.Id, RequiredString(result.RootElement, "id"), url);
        return url;
    }

    public async Task<string> PortalAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var customerId = billing.Balance(account.ClubId!).CustomerId;
        if (customerId is null) throw new BillingException("no_billing_customer", "Billing management becomes available after your first checkout.");
        using var result = await RequestAsync(HttpMethod.Post, "billing_portal/sessions", new() { ["customer"] = customerId, ["return_url"] = SiteUrl + "/archive.html" }, null, cancellationToken);
        var url = RequiredString(result.RootElement, "url"); ValidateStripeRedirect(url, "billing.stripe.com"); return url;
    }

    public async Task<string> IntegrationCheckoutAsync(AccountRecord account, string requestId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestId, out _)) throw new BillingException("invalid_request", "A checkout request identifier is required.", 400);
        var offer = await IntegrationOfferAsync(cancellationToken, refresh: true);
        if (!offer.Available) throw new BillingException("integration_unavailable", offer.AvailabilityReason ?? "The Intelligent Golf integration is not available to purchase yet.", 503);
        var gate = integrationCheckoutGates.GetOrAdd(account.ClubId!, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (billing.IntegrationSubscription(account.ClubId!).Current) throw AlreadySubscribed();
            var customer = await CustomerAsync(account, cancellationToken);
            var order = billing.GetOrCreateIntegrationCheckout(account.ClubId!, IntegrationPrice);
            if (order.PriceId != IntegrationPrice) throw new BillingException("integration_checkout_review", "A previous integration checkout needs support to review before the configured price can change.");
            if (order.CheckoutId is not null)
            {
                using var prior = await RequestAsync(HttpMethod.Get, "checkout/sessions/" + Uri.EscapeDataString(order.CheckoutId), null, null, cancellationToken);
                var session = prior.RootElement;
                if (String(session, "id") != order.CheckoutId || String(session, "mode") != "subscription" || Id(session, "customer") != customer ||
                    Metadata(session, "club_id") != account.ClubId || Metadata(session, "integration_order_id") != order.Id ||
                    !session.TryGetProperty("livemode", out var live) || live.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || live.GetBoolean() != (Mode == "live"))
                    throw new BillingException("integration_checkout_mismatch", "The existing integration checkout needs support to verify its ownership.");
                var status = String(session, "status");
                if (status == "open")
                {
                    var existingUrl = RequiredString(session, "url"); ValidateStripeRedirect(existingUrl, "checkout.stripe.com"); return existingUrl;
                }
                if (status == "complete")
                {
                    var subscriptionId = Id(session, "subscription");
                    if (subscriptionId is null) throw new BillingException("integration_payment_pending", "Your integration payment is still being confirmed. Manage billing before starting another checkout.");
                    var subscriptionStatus = await RefreshSubscriptionAsync(subscriptionId, cancellationToken);
                    if (subscriptionStatus is not ("canceled" or "incomplete_expired")) throw AlreadySubscribed();
                }
                else if (status != "expired") throw new BillingException("integration_payment_pending", "The previous integration checkout is still being confirmed.");
                billing.ExpireIntegrationCheckout(order.Id);
                order = billing.GetOrCreateIntegrationCheckout(account.ClubId!, IntegrationPrice);
            }
            // Stripe retains idempotency keys for at least 24 hours. An older unknown response
            // must be reconciled by support, never silently retried as a second subscription.
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - order.CreatedAt > 23 * 60 * 60)
                throw new BillingException("integration_checkout_review", "The outcome of an earlier integration checkout needs support to review before trying again.");
            using var result = await RequestAsync(HttpMethod.Post, "checkout/sessions", new()
            {
                ["mode"] = "subscription", ["customer"] = customer,
                ["success_url"] = SiteUrl + "/archive.html?billing=success", ["cancel_url"] = SiteUrl + "/archive.html?billing=cancelled",
                ["expires_at"] = (order.CreatedAt + 24 * 60 * 60).ToString(CultureInfo.InvariantCulture),
                ["line_items[0][price]"] = order.PriceId, ["line_items[0][quantity]"] = "1",
                ["metadata[club_id]"] = account.ClubId!, ["metadata[integration_order_id]"] = order.Id,
                ["subscription_data[metadata][club_id]"] = account.ClubId!, ["subscription_data[metadata][integration_order_id]"] = order.Id,
                ["payment_method_types[0]"] = "card"
            }, "integration-order:" + order.Id, cancellationToken);
            var url = RequiredString(result.RootElement, "url"); ValidateStripeRedirect(url, "checkout.stripe.com");
            billing.AttachIntegrationCheckout(order.Id, RequiredString(result.RootElement, "id"), url);
            return url;
        }
        finally { gate.Release(); }
    }

    private static BillingException AlreadySubscribed() => new("already_subscribed", "This club already has an integration subscription or a payment awaiting confirmation. Manage it through billing.");

    private async Task<string> CustomerAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        var existing = billing.Balance(account.ClubId!).CustomerId;
        if (existing != null) return existing;
        using var customer = await RequestAsync(HttpMethod.Post, "customers", new() { ["email"] = account.Email, ["metadata[club_id]"] = account.ClubId! }, "club-customer:" + account.ClubId, cancellationToken);
        var id = RequiredString(customer.RootElement, "id"); billing.SetCustomer(account.ClubId!, id); return billing.Balance(account.ClubId!).CustomerId!;
    }

    public async Task HandleWebhookAsync(byte[] body, string signature, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (!VerifySignature(body, signature, WebhookSecret, DateTimeOffset.UtcNow)) throw new BillingException("invalid_signature", "Invalid webhook signature.", 400);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("livemode", out var live) || live.GetBoolean() != (Mode == "live")) throw new BillingException("wrong_mode", "Webhook mode mismatch.", 400);
        var eventId = RequiredString(root, "id"); var type = RequiredString(root, "type"); var obj = root.GetProperty("data").GetProperty("object");
        // Serialise retrieve-and-apply, so delayed subscription events cannot overwrite newer state.
        await webhookGate.WaitAsync(cancellationToken);
        try
        {
            if (type is "checkout.session.completed" or "checkout.session.async_payment_succeeded" or "checkout.session.expired")
            {
                using var sessionResult = await RequestAsync(HttpMethod.Get, "checkout/sessions/" + Uri.EscapeDataString(RequiredString(obj, "id")), null, null, cancellationToken);
                var session = sessionResult.RootElement;
                if (String(session, "mode") == "subscription")
                {
                    var subscriptionId = Id(session, "subscription");
                    if (subscriptionId != null) await RefreshSubscriptionAsync(subscriptionId, cancellationToken);
                    return;
                }
                var purchaseId = Metadata(session, "purchase_id");
                if (purchaseId is null) return;
                if (String(session, "status") == "expired") { billing.ExpirePurchase(eventId, purchaseId); return; }
                if (String(session, "payment_status") != "paid" || String(session, "status") != "complete") return;
                var purchase = billing.FindPurchase(purchaseId) ?? throw new BillingException("unknown_purchase", "No stored order exists for this payment.", 400);
                if (Metadata(session, "club_id") != purchase.ClubId || Id(session, "customer") != billing.Balance(purchase.ClubId).CustomerId) throw new BillingException("wrong_customer", "Payment customer mismatch.", 400);
                billing.FulfilPayment(eventId, purchaseId, RequiredString(session, "id"), Id(session, "payment_intent") ?? "", session.GetProperty("amount_total").GetInt64(), RequiredString(session, "currency"), Id(session, "customer")!);
            }
            else if (type == "charge.refunded" || type.StartsWith("charge.dispute.", StringComparison.Ordinal))
            {
                var paymentId = Id(obj, "payment_intent");
                if (paymentId is null && Id(obj, "charge") is { } chargeId)
                {
                    using var charge = await RequestAsync(HttpMethod.Get, "charges/" + Uri.EscapeDataString(chargeId), null, null, cancellationToken);
                    paymentId = Id(charge.RootElement, "payment_intent");
                }
                if (paymentId != null) billing.HoldPayment(eventId, paymentId, type, type == "charge.refunded" && obj.TryGetProperty("refunded", out var refunded) && refunded.GetBoolean());
            }
            else if (type.StartsWith("customer.subscription.", StringComparison.Ordinal)) await RefreshSubscriptionAsync(RequiredString(obj, "id"), cancellationToken);
            else if (type is "invoice.paid" or "invoice.payment_failed")
            {
                if (Id(obj, "subscription") is { } subscriptionId) await RefreshSubscriptionAsync(subscriptionId, cancellationToken);
            }
        }
        finally { webhookGate.Release(); }
    }

    private async Task<string?> RefreshSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        using var result = await RequestAsync(HttpMethod.Get, "subscriptions/" + Uri.EscapeDataString(id) + "?expand[]=latest_invoice", null, null, cancellationToken);
        var sub = result.RootElement; var club = Metadata(sub, "club_id");
        if (club is null) return null;
        var customer = Id(sub, "customer");
        if (customer != billing.Balance(club).CustomerId) throw new BillingException("wrong_customer", "Subscription customer mismatch.", 400);
        var items = sub.GetProperty("items").GetProperty("data");
        var priceId = items.GetArrayLength() == 1 ? Id(items[0], "price") : null;
        var status = RequiredString(sub, "status");
        var paid = sub.TryGetProperty("latest_invoice", out var invoice) && invoice.ValueKind == JsonValueKind.Object && String(invoice, "status") == "paid";
        var periodEnd = sub.TryGetProperty("current_period_end", out var end) ? end.GetInt64() : 0;
        billing.SyncSubscription(id, club, status == "active" && paid && priceId == IntegrationPrice ? "active" : "inactive", periodEnd, priceId ?? "", status);
        return status;
    }

    public static bool VerifySignature(byte[] body, string header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || header.Length > 4096) return false;
        var values = header.Split(',').Select(x => x.Trim().Split('=', 2)).Where(x => x.Length == 2).ToArray();
        var timestamps = values.Where(x => x[0] == "t").ToArray();
        if (timestamps.Length != 1 || !long.TryParse(timestamps[0][1], NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) || timestamp < now.ToUnixTimeSeconds() - 300 || timestamp > now.ToUnixTimeSeconds() + 300) return false;
        var prefix = Encoding.UTF8.GetBytes(timestamps[0][1] + ".");
        var signed = new byte[prefix.Length + body.Length]; prefix.CopyTo(signed, 0); body.CopyTo(signed, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        foreach (var value in values.Where(x => x[0] == "v1"))
        {
            if (value[1].Length != 64) continue;
            try { if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(value[1]))) return true; }
            catch (FormatException) { }
        }
        return false;
    }

    private async Task<JsonDocument> RequestAsync(HttpMethod method, string path, Dictionary<string, string>? values, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, "https://api.stripe.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        request.Headers.Add("Stripe-Version", "2025-02-24.acacia");
        if (idempotencyKey != null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (values != null) request.Content = new FormUrlEncodedContent(values);
        using var response = await clients.CreateClient(nameof(StripeBillingService)).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new BillingException("payment_provider_error", "The payment provider could not complete this request. Retry the same checkout or contact support.", 502);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }
    private static void ValidateStripeRedirect(string url, string host) { if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != host) throw new BillingException("invalid_payment_url", "The payment provider returned an unexpected checkout address.", 502); }
    private static string? String(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string RequiredString(JsonElement element, string key) => String(element, key) ?? throw new BillingException("invalid_payment_event", "A required payment field is missing.", 400);
    private static string? Id(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Object ? String(value, "id") : null : null;
    private static string? Metadata(JsonElement element, string key) => element.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object ? String(metadata, key) : null;
}
