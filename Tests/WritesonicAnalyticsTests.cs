using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class WritesonicAnalyticsTests
{
    private sealed class RecordingHandler : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Bodies { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(WritesonicAnalytics.Endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(Status);
        }
    }
    private static WritesonicAnalytics Service(RecordingHandler handler, bool enabled = true, bool render = true) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PUBLIC_SITE_URL"] = "https://trophy.guru", ["WRITESONIC_ANALYTICS_API_KEY"] = enabled ? "test-key" : null,
            ["RENDER"] = render ? "true" : "false"
        }).Build(), handler, NullLogger<WritesonicAnalytics>.Instance);
    private static DefaultHttpContext Context(string path = "/for-golf-clubs", string ua = "OAI-SearchBot/1.0")
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("trophy.guru");
        context.Request.Scheme = "https";
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Headers.UserAgent = ua;
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers.Origin = "https://trophy.guru";
        context.Request.Headers.Referer = "https://trophy.guru" + path;
        return context;
    }
    [Theory]
    [InlineData("/archive.html")]
    [InlineData("/account-security.html")]
    [InlineData("/api/auth/status")]
    [InlineData("/admin")]
    [InlineData("/honours/private-club")]
    [InlineData("/honours.html")]
    [InlineData("/uploads/private.jpg")]
    [InlineData("/blog/images/private.jpg")]
    [InlineData("/health")]
    [InlineData("/blog/article?token=secret")]
    [InlineData("/blog/../admin")]
    [InlineData("//attacker.example")]
    public async Task PrivateAndArbitraryPathsNeverLeaveServer(string path)
    {
        var handler = new RecordingHandler(); using var service = Service(handler);
        await service.ObserveCrawlerAsync(Context(path), _ => Task.CompletedTask);
        Assert.Null(service.CreateEvent(Context(), path, "", "GET", 200));
        await service.FlushAsync(default);
        Assert.Empty(handler.Bodies);
    }
    [Fact]
    public async Task PublicCrawlerUsesActualStatusAndSanitizedMetadataWithoutBlockingPage()
    {
        var handler = new RecordingHandler(); using var service = Service(handler);
        var context = Context("/blog/missing-guide");
        context.Request.QueryString = new QueryString("?token=secret&email=private@example.com");
        context.Request.Headers.Referer = "https://chatgpt.com/c/private-conversation?token=secret";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7, garbage, 192.0.2.4";
        context.Request.Headers["CF-IPCountry"] = "GB";
        await service.ObserveCrawlerAsync(context, c => { c.Response.StatusCode = 404; return Task.CompletedTask; });
        Assert.Empty(handler.Bodies); // No external request in the page-serving path.
        Assert.True(await service.FlushAsync(default));
        var raw = Assert.Single(handler.Bodies);
        Assert.DoesNotContain("secret", raw); Assert.DoesNotContain("private", raw);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        var entry = json.RootElement[0];
        Assert.Equal("https://trophy.guru/blog/missing-guide", entry.GetProperty("url").GetString());
        Assert.Equal("https://chatgpt.com/", entry.GetProperty("referrer").GetString());
        Assert.Equal("404", entry.GetProperty("response_status").GetString());
        Assert.Equal("203.0.113.7", entry.GetProperty("ip").GetString());
        Assert.Equal("GB", entry.GetProperty("country_code").GetString());
        Assert.Equal("ok", service.Status.State); Assert.NotNull(service.Status.LastAcceptedAt);
    }
    [Fact]
    public async Task OrdinaryBrowserRequestsDoNotReportWithoutExplicitConsent()
    {
        var handler = new RecordingHandler(); using var service = Service(handler);
        var context = Context(ua: "Mozilla/5.0");
        await service.ObserveCrawlerAsync(context, _ => Task.CompletedTask);
        var denied = service.ReceiveVisit(context, new("/for-golf-clubs", "https://chatgpt.com/", false));
        Assert.Equal(400, ((IStatusCodeHttpResult)denied).StatusCode);
        await service.FlushAsync(default); Assert.Empty(handler.Bodies);
        var accepted = service.ReceiveVisit(context, new("/for-golf-clubs", "https://chatgpt.com/private?secret=value", true));
        Assert.Equal(204, ((IStatusCodeHttpResult)accepted).StatusCode);
        await service.FlushAsync(default); Assert.Single(handler.Bodies);
        Assert.DoesNotContain("private", handler.Bodies[0]);
    }
    [Fact]
    public async Task CrossSiteRequestsAndForgedPrivatePathsAreRejected()
    {
        var handler = new RecordingHandler(); using var service = Service(handler);
        var context = Context(ua: "Mozilla/5.0");
        context.Request.Method = "POST";
        context.Request.Headers.Origin = "https://attacker.example";
        Assert.Equal(403, ((IStatusCodeHttpResult)service.ReceiveVisit(context, new("/for-golf-clubs", "", true))).StatusCode);
        context.Request.Headers.Origin = "https://trophy.guru";
        Assert.Equal(400, ((IStatusCodeHttpResult)service.ReceiveVisit(context, new("/archive.html", "", true))).StatusCode);
        Assert.Equal(400, ((IStatusCodeHttpResult)service.ReceiveVisit(context, new("/about", "", true))).StatusCode);
        await service.FlushAsync(default); Assert.Empty(handler.Bodies);
    }
    [Fact]
    public async Task MissingKeyDisablesReportingAndUnknownHostIsExcluded()
    {
        var handler = new RecordingHandler(); using var disabled = Service(handler, enabled: false);
        await disabled.ObserveCrawlerAsync(Context(), _ => Task.CompletedTask);
        await disabled.FlushAsync(default); Assert.Empty(handler.Bodies);
        using var service = Service(handler);
        var context = Context(); context.Request.Host = new HostString("preview.onrender.com");
        Assert.Null(service.CreateEvent(context, "/", "", "GET", 200));
    }
    [Fact]
    public async Task DeliveryFailureIsVisibleAndDoesNotReplayAmbiguousRequests()
    {
        var handler = new RecordingHandler { Status = HttpStatusCode.Unauthorized }; using var service = Service(handler);
        await service.ObserveCrawlerAsync(Context(), _ => Task.CompletedTask);
        Assert.False(await service.FlushAsync(default)); Assert.Equal("http_401", service.Status.State);
        Assert.Null(service.Status.LastAcceptedAt);
        await service.FlushAsync(default); Assert.Single(handler.Bodies);
        handler.Status = HttpStatusCode.OK;
        await service.ObserveCrawlerAsync(Context(), _ => Task.CompletedTask);
        Assert.True(await service.FlushAsync(default)); Assert.Equal("ok", service.Status.State);
    }
    [Fact]
    public void QueueIsBoundedAndProxyHeadersAreIgnoredOutsideRender()
    {
        var handler = new RecordingHandler(); using var service = Service(handler, render: false);
        var context = Context(); context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        var entry = service.CreateEvent(context, "/", "", "GET", 200)!;
        Assert.Equal("127.0.0.1", entry.Ip); Assert.Empty(entry.ForwardedFor);
        for (var i = 0; i < 512; i++) Assert.True(service.Enqueue(entry));
        Assert.False(service.Enqueue(entry));
    }
}
