using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Trophy.Catalogue.Services;

// Public discovery traffic only. Never reads an account or archive store.
public sealed class WritesonicAnalytics(IConfiguration config, IHttpClientFactory clients,
    ILogger<WritesonicAnalytics> logger) : BackgroundService
{
    public const string ClientName = "WritesonicAnalytics";
    public const string Endpoint = "https://ingestion.writesonic.com/api/v1/analytics/ingest";
    public const string VisitPath = "/api/public/analytics/visit";
    private readonly Channel<TrafficEvent> queue = Channel.CreateBounded<TrafficEvent>(new BoundedChannelOptions(512)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private DeliveryStatus status = new(false, "disabled");
    public bool Enabled => !string.IsNullOrWhiteSpace(config["WRITESONIC_ANALYTICS_API_KEY"])
        && !string.Equals(config["WRITESONIC_ANALYTICS_ENABLED"], "false", StringComparison.OrdinalIgnoreCase);
    public DeliveryStatus Status => Volatile.Read(ref status) with { Enabled = Enabled };
    public record DeliveryStatus(bool Enabled, string State, DateTimeOffset? LastAcceptedAt = null);
    public record VisitInput(string? Path, string? Referrer, bool Consent);
    public sealed record TrafficEvent(
        [property: JsonPropertyName("ip")] string Ip,
        [property: JsonPropertyName("x_real_ip")] string RealIp,
        [property: JsonPropertyName("ua")] string UserAgent,
        [property: JsonPropertyName("country_code")] string CountryCode,
        [property: JsonPropertyName("referrer")] string Referrer,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("x_forwarded_for")] string ForwardedFor,
        [property: JsonPropertyName("response_status")] string ResponseStatus);

    private static readonly string[] BotNames = ["GPTBot", "OAI-SearchBot", "ChatGPT-User", "PerplexityBot", "Perplexity-User",
        "ClaudeBot", "Claude-User", "Claude-SearchBot", "anthropic-ai", "cohere-ai", "Googlebot", "Google-Extended",
        "bingbot", "Applebot", "Amazonbot", "Bytespider", "meta-externalagent", "meta-externalfetcher", "FacebookBot"];
    // These are claimed crawler identities, not verified bot ownership.
    public static bool IsCrawler(string userAgent) => BotNames.Any(name => userAgent.Contains(name, StringComparison.OrdinalIgnoreCase));

    public static bool IsPublicPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || !path.StartsWith('/') || path.Contains("//")) return false;
        var clean = path.TrimEnd('/');
        if (clean is "" or "/index.html" or "/privacy.html" or "/blog" or "/robots.txt" or "/sitemap.xml" or "/llms.txt") return true;
        if (ProductPages.All.Any(page => page.Path.Equals(clean, StringComparison.OrdinalIgnoreCase))) return true;
        if (clean is "/integrations/intelligent-golf" or "/uk/how-to-catalogue-trophy-winners" or "/us/how-to-catalog-trophy-winners") return true;
        return Regex.IsMatch(clean, @"\A/blog/[a-z0-9-]{1,200}\z", RegexOptions.CultureInvariant);
    }

    public static string ReferrerOrigin(string? value) => value?.Length <= 2048 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority) + "/" : "";

    public TrafficEvent? CreateEvent(HttpContext context, string path, string? referrer, string method, int responseStatus)
    {
        if (!Enabled || !IsPublicPath(path)) return null;
        var origin = BlogEndpoints.PublicOrigin(config);
        var expectedHost = new Uri(origin).Host;
        if (!context.Request.Host.Host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) &&
            !context.Request.Host.Host.Equals("www." + expectedHost, StringComparison.OrdinalIgnoreCase)) return null;
        // Proxy metadata is used for reporting only, never for authentication.
        var onRender = string.Equals(config["RENDER"], "true", StringComparison.OrdinalIgnoreCase);
        var forwarded = onRender ? context.Request.Headers["X-Forwarded-For"].ToString().Split(',').Take(8)
            .Select(ParseIp).Where(ip => ip.Length > 0).ToArray() : [];
        var realIp = onRender ? ParseIp(context.Request.Headers["CF-Connecting-IP"].ToString()) : "";
        var ip = realIp.Length > 0 ? realIp : forwarded.FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "";
        var country = onRender ? context.Request.Headers["CF-IPCountry"].ToString().ToUpperInvariant() : "";
        if (!Regex.IsMatch(country, @"\A[A-Z]{2}\z") || country is "XX" or "T1") country = "";
        return new(ip, realIp, Limit(context.Request.Headers.UserAgent.ToString(), 2048), country,
            ReferrerOrigin(referrer), origin + path, method, string.Join(",", forwarded), responseStatus.ToString(CultureInfo.InvariantCulture));
    }

    public bool Enqueue(TrafficEvent entry) => Enabled && queue.Writer.TryWrite(entry);
    private static string ParseIp(string value) => IPAddress.TryParse(value.Trim(), out var ip) ? ip.ToString() : "";
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    public async Task ObserveCrawlerAsync(HttpContext context, RequestDelegate next)
    {
        if (!Enabled || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) ||
            !IsPublicPath(context.Request.Path.Value) || !IsCrawler(context.Request.Headers.UserAgent.ToString()))
        { await next(context); return; }
        var completed = false;
        try { await next(context); completed = true; }
        finally
        {
            var entry = CreateEvent(context, context.Request.Path.Value!, context.Request.Headers.Referer.ToString(),
                context.Request.Method, completed ? context.Response.StatusCode : 500);
            if (entry is not null) Enqueue(entry);
        }
    }

    public IResult ReceiveVisit(HttpContext context, VisitInput input)
    {
        if (!RequestSecurity.IsSameOriginMutation(context.Request, config)) return Results.StatusCode(403);
        if (!input.Consent || !IsPublicPath(input.Path)) return Results.BadRequest();
        if (!Uri.TryCreate(context.Request.Headers.Referer.ToString(), UriKind.Absolute, out var page) ||
            page.AbsolutePath != input.Path) return Results.BadRequest();
        // JS-capable crawlers have already been recorded by server middleware.
        if (!IsCrawler(context.Request.Headers.UserAgent.ToString()))
        {
            var entry = CreateEvent(context, input.Path!, input.Referrer, "GET", 200);
            if (entry is not null) Enqueue(entry);
        }
        return Results.NoContent();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Volatile.Write(ref status, new(Enabled, Enabled ? "waiting_for_traffic" : "disabled"));
        if (!Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                if (!await FlushAsync(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        var batch = new List<TrafficEvent>(50);
        while (batch.Count < 50 && queue.Reader.TryRead(out var entry)) batch.Add(entry);
        if (batch.Count == 0) return true;
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("x-api-key", config["WRITESONIC_ANALYTICS_API_KEY"]);
            request.Content = JsonContent.Create(batch);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) { SetFailure("http_" + (int)response.StatusCode); return false; }
            Volatile.Write(ref status, new(true, "ok", DateTimeOffset.UtcNow));
            logger.LogInformation("Writesonic analytics accepted {Count} public traffic events.", batch.Count);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        { SetFailure("delivery_failed"); return false; }
        // Best effort: discard failed batches to avoid duplicates after ambiguous timeouts.
    }

    private void SetFailure(string state)
    {
        Volatile.Write(ref status, Status with { State = state });
        // Never log keys, payloads, client IPs or provider response bodies.
        logger.LogWarning("Writesonic analytics {State}; public-page serving is unaffected.", state);
    }
}
