using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class AutoSeoBlogTests : IDisposable
{
    private const string Token = "test-only-webhook-secret";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trophy-blog-test-" + Guid.NewGuid().ToString("N"));
    private readonly BlogStore store;
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().ConfigureHttpJsonOptions(_ => { }).BuildServiceProvider();
    private readonly IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["AUTOSEO_WEBHOOK_TOKEN"] = Token, ["PUBLIC_SITE_URL"] = "https://trophy.guru" }).Build();
    public AutoSeoBlogTests() => store = new BlogStore(directory);
    private static AutoSeoArticle Payload(long id = 42) => new()
    {
        Event = "article.published", Id = id, Title = "Preserving your trophies", Slug = "preserving-your-trophies",
        ContentHtml = "<h2>A lasting record</h2><p>Keep every winner.</p>", ContentMarkdown = "Keep every winner.",
        MetaDescription = "A guide to club history.", Status = "published",
        CreatedAt = DateTimeOffset.Parse("2026-09-01T10:00:00Z"), PublishedAt = DateTimeOffset.Parse("2026-09-02T10:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-09-02T10:00:00Z"), Keywords = ["trophies"],
        FaqSchema = [new("How?", "Take a photograph.")]
    };

    private async Task<(int Status, string Body)> Deliver(string json, string? auth = "Bearer " + Token,
        string? signature = null, HttpMessageHandler? handler = null, IConfiguration? settings = null, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST"; context.Request.Path = BlogEndpoints.WebhookPath;
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Response.Body = new MemoryStream();
        if (auth is not null) context.Request.Headers.Authorization = auth;
        if (signature is not null) context.Request.Headers["X-AutoSEO-Signature"] = signature;
        using var client = new HttpClient(handler ?? new ImageHandler());
        var result = await BlogEndpoints.ReceiveAsync(context, store, new BlogImages(client), settings ?? config, services.GetRequiredService<ILoggerFactory>());
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
    private Task<(int Status, string Body)> Deliver(AutoSeoArticle article, HttpMessageHandler? handler = null) => Deliver(JsonSerializer.Serialize(article, BlogStore.Json), handler: handler);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Bearer wrong")] [InlineData("bearer test-only-webhook-secret")]
    public async Task MissingOrWrongAuthorizationIsRejectedBeforePublishing(string? authorization)
    {
        Assert.Equal(401, (await Deliver("{\"event\":\"test\"}", authorization)).Status);
        Assert.Empty(store.List());
    }
    [Fact] public async Task TestDeliveryReturnsHttpsUrlWithoutCreatingPost()
    {
        var response = await Deliver("{\"event\":\"test\"}");
        Assert.Equal(200, response.Status); Assert.Equal("https://trophy.guru/test", JsonDocument.Parse(response.Body).RootElement.GetProperty("url").GetString());
        Assert.Empty(store.List()); Assert.Empty(Directory.GetFiles(store.ImageDirectory));
    }
    [Fact] public async Task HmacUsesExactUtf8BytesIncludingWhitespace()
    {
        const string raw = "{\n  \"event\" : \"test\", \"title\": \"café 🏆\"\n}";
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Token), Encoding.UTF8.GetBytes(raw)));
        Assert.Equal(200, (await Deliver(raw, signature: signature)).Status);
        Assert.Equal(401, (await Deliver(raw + " ", signature: signature)).Status);
        Assert.Equal(401, (await Deliver(raw, signature: new string('z', 64))).Status);
        Assert.Equal(401, (await Deliver(raw, signature: "")).Status);
    }
    [Fact] public async Task RequiredSignatureAndMissingConfigurationFailClosed()
    {
        var required = new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?> { ["AUTOSEO_REQUIRE_SIGNATURE"] = "true" }).Build();
        Assert.Equal(401, (await Deliver("{\"event\":\"test\"}", settings: required)).Status);
        Assert.Equal(503, (await Deliver("{\"event\":\"test\"}", settings: new ConfigurationBuilder().Build())).Status);
    }
    [Fact] public async Task PublishDuplicateUpdateAndDelayedDeliveryKeepOnePostAndStableUrl()
    {
        var original = Payload();
        var published = await Deliver(original); Assert.Equal(200, published.Status);
        Assert.Equal(200, (await Deliver(original)).Status);
        var updated = original with { Event = "article.updated", Slug = "a-renamed-slug", Title = "Updated title", ContentHtml = "<p>Updated body</p>", UpdatedAt = original.UpdatedAt.AddHours(1) };
        var update = await Deliver(updated); Assert.Equal(published.Body, update.Body);
        Assert.Equal(200, (await Deliver(original)).Status);
        var stored = Assert.Single(store.List()); Assert.Equal("Updated title", stored.Article.Title);
        Assert.Equal("preserving-your-trophies", stored.Article.Slug); Assert.Contains("Updated body", stored.Html);
        Assert.Equal(original.ContentMarkdown, stored.Article.ContentMarkdown);
    }
    [Fact] public async Task UpdateCanArriveFirstAndSlugCollisionsCannotOverwriteAnotherId()
    {
        Assert.Equal(200, (await Deliver(Payload() with { Event = "article.updated" })).Status);
        var translated = await Deliver(Payload(43) with { LanguageCode = "es", SourceArticleId = 42 });
        Assert.Equal(200, translated.Status); Assert.Contains("preserving-your-trophies-43", translated.Body);
        Assert.Equal(2, store.List().Count);
    }
    [Fact] public async Task ConcurrentDuplicateDeliveriesAreIdempotent()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Deliver(Payload())));
        Assert.All(responses, response => Assert.Equal(200, response.Status)); Assert.Single(store.List());
    }
    [Fact] public async Task ImagesAreLocalAndFailedUpdateKeepsPreviousPublication()
    {
        var article = Payload() with { HeroImageUrl = "https://images.example/hero.png", HeroImageAlt = "A silver cup", InfographicImageUrl = "https://images.example/chart.png", ContentHtml = "<p>Hello</p><img src=\"https://images.example/hero.png\"><img src=\"https://images.example/chart.png\">" };
        var handler = new ImageHandler(); Assert.Equal(200, (await Deliver(article, handler)).Status); Assert.Equal(2, handler.Requests);
        var stored = store.Find(42)!; Assert.StartsWith("/blog/images/", stored.HeroPath); Assert.DoesNotContain("https://images.example", stored.Html);
        Assert.Contains("alt=\"A silver cup\"", stored.Html); Assert.True(File.Exists(Path.Combine(store.ImageDirectory, Path.GetFileName(stored.HeroPath!))));
        Assert.Equal(500, (await Deliver(article with { UpdatedAt = article.UpdatedAt.AddHours(1), Title = "Must not be saved" }, new ImageHandler(true))).Status);
        Assert.Equal(article.Title, store.Find(42)!.Article.Title);
    }
    [Fact] public async Task InvalidPayloadsAreRejectedWithoutSaving()
    {
        Assert.Equal(400, (await Deliver("not json")).Status);
        Assert.Equal(400, (await Deliver(Payload() with { Event = "article.deleted" })).Status);
        Assert.Equal(400, (await Deliver(Payload() with { Slug = "../../archive" })).Status);
        Assert.Equal(400, (await Deliver(Payload() with { Id = 0 })).Status);
        Assert.Equal(415, (await Deliver("{}", contentType: "text/plain")).Status);
        Assert.Equal(413, (await Deliver(new string(' ', BlogEndpoints.MaxBodyBytes + 1))).Status);
        Assert.Empty(store.List());
    }
    [Fact] public void SanitizerRemovesActiveContentAndHotlinks()
    {
        var html = BlogEndpoints.SanitizeHtml(Payload() with { ContentHtml = "<script>alert(1)</script><iframe src='/archive.html'></iframe><p onclick='bad()' style='background:url(https://evil.example/x)'>Text</p><a href='javascript:bad()'>Link</a><img src='https://evil.example/x'><svg onload='bad()'></svg><a href='https://example.com'>Source</a>" }, null, null);
        Assert.DoesNotContain("script", html); Assert.DoesNotContain("onclick", html); Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("iframe", html); Assert.DoesNotContain("evil.example", html); Assert.DoesNotContain("svg", html);
        Assert.Contains("https://example.com", html); Assert.Contains("Text", html);
    }
    [Fact] public async Task RenderEncodesMetadataAndIncludesStructuredData()
    {
        await Deliver(Payload() with { Title = "<script>bad()</script>", FaqSchema = [new("Question?", "</script><script>bad()</script>")] });
        var html = BlogPages.Article(store.Find(42)!, "https://trophy.guru", "test-nonce");
        Assert.DoesNotContain("<script>bad()", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("BlogPosting", html); Assert.Contains("FAQPage", html); Assert.Contains("nonce=\"test-nonce\"", html);
        Assert.Contains("https://trophy.guru/blog/preserving-your-trophies", html);
        Assert.Contains("Frequently asked questions", html);
    }
    [Theory]
    [InlineData("127.0.0.1", false)] [InlineData("10.0.0.1", false)] [InlineData("172.16.1.1", false)]
    [InlineData("192.168.1.1", false)] [InlineData("169.254.169.254", false)] [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)] [InlineData("::ffff:127.0.0.1", false)] [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)] [InlineData("2002:7f00:1::", false)] [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void ImageDownloadsRejectPrivateAndTransitionAddresses(string ip, bool expected) => Assert.Equal(expected, BlogImages.IsPublicAddress(IPAddress.Parse(ip)));
    [Fact] public async Task ImageRedirectToPrivateAddressIsRejected()
    {
        using var client = new HttpClient(new RedirectHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => new BlogImages(client).DownloadAsync("https://images.example/x", store.ImageDirectory, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(store.ImageDirectory));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task InvalidOrOversizedImageReturnsRetryableErrorWithoutPublishing(bool oversized)
    {
        var response = await Deliver(Payload() with { HeroImageUrl = "https://images.example/photo.png" }, new InvalidImageHandler(oversized));
        Assert.Equal(500, response.Status); Assert.Empty(store.List());
    }
    [Fact] public void FakeImageAndSvgAreRejected()
    {
        Assert.Throws<HttpRequestException>(() => BlogImages.ImageExtension(Encoding.UTF8.GetBytes("<svg onload='bad()'/>")));
        Assert.Throws<HttpRequestException>(() => BlogImages.ImageExtension(Encoding.UTF8.GetBytes("<html>error</html>")));
    }
    [Fact] public void WebhookDoesNotRequireBrowserOriginButOtherApisStillDo()
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = BlogEndpoints.WebhookPath;
        Assert.True(RequestSecurity.IsSameOriginMutation(context.Request, config));
        context.Request.Path = "/api/trophies"; Assert.False(RequestSecurity.IsSameOriginMutation(context.Request, config));
    }
    [Fact] public async Task ReinitializationPreservesBlogRowsAndExistingDatabaseTables()
    {
        using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "autoseo.sqlite")};Pooling=False"))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS preservation_fixture(value TEXT); INSERT INTO preservation_fixture(value) VALUES('keep me');"; command.ExecuteNonQuery();
        }
        await Deliver(Payload());
        var reopened = new BlogStore(directory); Assert.Single(reopened.List());
        using var verify = new SqliteConnection($"Data Source={Path.Combine(directory, "autoseo.sqlite")};Pooling=False"); verify.Open();
        using var read = verify.CreateCommand(); read.CommandText = "SELECT value FROM preservation_fixture";
        Assert.Equal("keep me", read.ExecuteScalar());
    }
    public void Dispose() { services.Dispose(); Directory.Delete(directory, recursive: true); }
    private sealed class ImageHandler(bool fail = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = new HttpResponseMessage(fail ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")) };
            response.Content.Headers.ContentType = new("image/png"); return Task.FromResult(response);
        }
    }
    private sealed class InvalidImageHandler(bool oversized) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new MemoryStream(oversized ? new byte[BlogImages.MaxBytes + 1] : Encoding.UTF8.GetBytes("<html>not an image</html>"))) };
            response.Content.Headers.ContentType = new("image/png");
            return Task.FromResult(response);
        }
    }
    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://127.0.0.1/private"); return Task.FromResult(response);
        }
    }
}

