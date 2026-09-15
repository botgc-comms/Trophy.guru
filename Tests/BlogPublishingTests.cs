using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class BlogPublishingTests : IDisposable
{
    private const string Token = "test-only-publishing-key-over-32-characters";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trophy-publish-test-" + Guid.NewGuid().ToString("N"));
    private readonly BlogStore store;
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().ConfigureHttpJsonOptions(_ => { }).BuildServiceProvider();
    private readonly IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["BLOG_PUBLISH_TOKEN"] = Token, ["PUBLIC_SITE_URL"] = "https://trophy.guru" }).Build();
    public BlogPublishingTests() => store = new BlogStore(directory);
    private static BlogPublishRequest Payload(string id = "writesonic-document-one") => new()
    {
        Mode = "publish", DocumentId = id, UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        Title = "A club archive", Slug = "a-club-archive",
        ContentHtml = "<h2>Keep the evidence</h2><p>Photograph, review and share the winners.</p>",
        MetaDescription = "A practical club archive."
    };
    private async Task<(int Status, string Body)> Deliver(BlogPublishRequest payload, string? auth = "Bearer " + Token,
        HttpMessageHandler? handler = null, IConfiguration? settings = null, string? raw = null, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = BlogPublishingEndpoints.WebhookPath; context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw ?? JsonSerializer.Serialize(payload, BlogStore.Json)));
        context.Response.Body = new MemoryStream();
        if (auth is not null) context.Request.Headers.Authorization = auth;
        using var client = new HttpClient(handler ?? new ImageHandler());
        var result = await BlogPublishingEndpoints.ReceiveAsync(context, store, new BlogImages(client), settings ?? config, services.GetRequiredService<ILoggerFactory>());
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData("Bearer wrong")]
    public async Task UnauthorizedRequestsCannotPublish(string? token)
    {
        Assert.Equal(401, (await Deliver(Payload(), token)).Status);
        Assert.Empty(store.List());
    }
    [Fact]
    public async Task MissingConfigurationTestAndValidationNeverPublishOrDownload()
    {
        Assert.Equal(503, (await Deliver(Payload(), settings: new ConfigurationBuilder().Build())).Status);
        Assert.Contains("connected", (await Deliver(new() { Mode = "test" })).Body);
        var images = new ImageHandler();
        var result = await Deliver(Payload() with { Mode = "validate", HeroImageUrl = "https://images.example.org/cover.png" }, handler: images);
        Assert.Equal(200, result.Status); Assert.Contains("validated", result.Body);
        Assert.Equal(0, images.Requests); Assert.Empty(store.List()); Assert.Empty(Directory.GetFiles(store.ImageDirectory));
    }
    [Fact]
    public async Task RetriesConcurrentDeliveryAndUpdatesPreserveIdentityAndFirstPublication()
    {
        var article = Payload();
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Deliver(article)));
        Assert.All(results, r => Assert.Equal(200, r.Status));
        var first = Assert.Single(store.List());
        Assert.True(first.Article.Id < 0);
        var updated = article with { Title = "Improved archive", Slug = "different-title", UpdatedAt = article.UpdatedAt.AddMinutes(1) };
        Assert.Equal(200, (await Deliver(updated)).Status);
        Assert.Contains("unchanged", (await Deliver(updated)).Body);
        Assert.Contains("ignored_older_revision", (await Deliver(article)).Body);
        var saved = Assert.Single(store.List());
        Assert.Equal("Improved archive", saved.Article.Title);
        Assert.Equal(first.Article.PublishedAt, saved.Article.PublishedAt);
        Assert.Equal(first.Article.Slug, saved.Article.Slug);
        Assert.Equal(saved.Article.PublishingDocumentId, new BlogStore(directory).Find(saved.Article.Id)!.Article.PublishingDocumentId);
    }
    [Fact]
    public async Task SameTimestampDifferentContentAndDifferentDocumentsCannotOverwrite()
    {
        var article = Payload();
        await Deliver(article);
        Assert.Equal(409, (await Deliver(article with { ContentHtml = "<p>Changed unexpectedly</p>" })).Status);
        Assert.Equal(409, (await Deliver(Payload("different-document"))).Status);
        Assert.Equal("A club archive", Assert.Single(store.List()).Article.Title);
    }
    [Fact]
    public async Task PositiveAutoSeoIdsRemainSeparateAndCannotBeReplacedBySlug()
    {
        var article = Payload("42");
        store.Upsert(new BlogPost(new AutoSeoArticle { Id = 42, Slug = "a-club-archive", Title = "Original AutoSEO",
            UpdatedAt = article.UpdatedAt, PublishedAt = article.UpdatedAt }, "<p>Original</p>", null, null));
        Assert.Equal(409, (await Deliver(article)).Status);
        Assert.Equal("Original AutoSEO", store.Find(42)!.Article.Title);
        Assert.Equal(200, (await Deliver(article with { Slug = "different-archive" })).Status);
        Assert.Equal(2, store.List().Count);
    }
    [Fact]
    public async Task PublishingUsesExistingStylingAndRemovesDocumentChromeUnsafeHtmlAndDuplicateTitle()
    {
        var article = Payload() with { ContentHtml = "<html><head><style>body{display:none}</style></head><body><nav>Bad menu</nav><main><h1>A club archive</h1><h1>Other heading</h1><p style='color:red' onclick='bad()'>Keep the evidence.</p><script>bad()</script><img src='https://example.org/unmapped.png'></main><footer>Bad footer</footer></body></html>" };
        Assert.Equal(200, (await Deliver(article)).Status);
        var saved = Assert.Single(store.List());
        Assert.DoesNotContain("<h1", saved.Html); Assert.Contains("<h2>Other heading</h2>", saved.Html);
        Assert.DoesNotContain("script", saved.Html); Assert.DoesNotContain("onclick", saved.Html);
        Assert.DoesNotContain("style", saved.Html); Assert.DoesNotContain("Bad menu", saved.Html);
        Assert.DoesNotContain("<img", saved.Html);
        var rendered = BlogPages.Article(saved, "https://trophy.guru", "test-nonce");
        Assert.Contains("BlogPosting", rendered);
        Assert.Contains("https://trophy.guru/blog/a-club-archive", rendered);
        Assert.Contains("Trophy Guru", rendered);
    }
    [Fact]
    public async Task ImageFailureDoesNotReplacePublishedArticle()
    {
        var original = Payload(); await Deliver(original);
        var broken = original with { UpdatedAt = original.UpdatedAt.AddMinutes(1), HeroImageUrl = "https://images.example.org/cover.png" };
        Assert.Equal(500, (await Deliver(broken, handler: new ImageHandler(true))).Status);
        Assert.Null(Assert.Single(store.List()).HeroPath);
        Assert.Equal(original.UpdatedAt, store.List()[0].Article.UpdatedAt);
        Assert.Equal(200, (await Deliver(broken)).Status);
        Assert.StartsWith("/blog/images/", Assert.Single(store.List()).HeroPath);
    }
    [Fact]
    public async Task InvalidEmptyAndOversizedPayloadsFailWithoutWriting()
    {
        var article = Payload();
        Assert.Equal(400, (await Deliver(article, raw: "{")).Status);
        Assert.Equal(415, (await Deliver(article, contentType: "text/plain")).Status);
        Assert.Equal(413, (await Deliver(article, raw: new string('x', BlogEndpoints.MaxBodyBytes + 1))).Status);
        Assert.Equal(400, (await Deliver(article with { UpdatedAt = default })).Status);
        Assert.Equal(400, (await Deliver(article with { Mode = "draft" })).Status);
        Assert.Equal(400, (await Deliver(article with { DocumentId = " " })).Status);
        Assert.Equal(400, (await Deliver(article with { Slug = "../../private" })).Status);
        Assert.Equal(400, (await Deliver(article with { ContentHtml = "<script>bad()</script>" })).Status);
        Assert.Equal(400, (await Deliver(article with { HeroImageUrl = "https://127.0.0.1/private" })).Status);
        Assert.Empty(store.List());
    }
    [Fact]
    public void OnlyExactAuthenticatedWebhookPathBypassesBrowserOriginRequirement()
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.Path = BlogPublishingEndpoints.WebhookPath;
        Assert.True(RequestSecurity.IsSameOriginMutation(context.Request, config));
        context.Request.Path += "/other";
        Assert.False(RequestSecurity.IsSameOriginMutation(context.Request, config));
    }

    [Theory]
    [InlineData("# Golf club records\n\nPhotograph each trophy in clear light. Record its name and check the engraved winners against the original photograph before sharing the archive.")]
    [InlineData("<html><head><title>Golf club records</title></head><body><h1>Golf club records</h1><p>Photograph each trophy in clear light. Record its name and check the engraved winners against the original photograph before sharing the archive.</p></body></html>")]
    [InlineData("Golf club records\nPhotograph each trophy in clear light. Record its name and check the engraved winners against the original photograph before sharing the archive.")]
    public async Task TextExportsPublishUsingExistingTemplateAndRetriesDoNotDuplicate(string text)
    {
        var payload = new BlogPublishRequest { Mode = "validate", Text = text };
        Assert.Equal(200, (await Deliver(payload)).Status);
        Assert.Empty(store.List());
        payload = payload with { Mode = "publish" };
        var published = await Deliver(payload);
        Assert.Equal(200, published.Status);
        Assert.Contains("published", published.Body);
        var first = Assert.Single(store.List());
        Assert.Equal("Golf club records", first.Article.Title);
        Assert.Equal("golf-club-records", first.Article.Slug);
        Assert.Contains("Photograph each trophy", first.Html);
        Assert.DoesNotContain("<h1", first.Html);
        Assert.Contains("unchanged", (await Deliver(payload)).Body);
        Assert.Equal(first.Article.PublishedAt, Assert.Single(store.List()).Article.PublishedAt);
        Assert.Equal(409, (await Deliver(payload with { Text = text.Replace("clear light", "daylight") })).Status);
        Assert.Single(store.List());
    }

    [Fact]
    public async Task TextExportsRejectShortSamplesAndRemoveUnsafeMarkup()
    {
        Assert.Equal(400, (await Deliver(new() { Mode = "publish", Text = "Sample text" })).Status);
        Assert.Empty(store.List());
        var text = "# A real archive\n\n<script>alert('unsafe')</script>\n\n"
            + "Photograph the original records and check each winner. Keep a copy of the source photograph so future club members can verify the information.\n\n"
            + "## Review\n\nCheck the **names** and years.";
        Assert.Equal(200, (await Deliver(new() { Mode = "publish", Text = text })).Status);
        var saved = Assert.Single(store.List());
        Assert.DoesNotContain("script", saved.Html);
        Assert.DoesNotContain("unsafe", saved.Html);
        Assert.Contains("<h2>Review</h2>", saved.Html);
        Assert.Contains("<strong>names</strong>", saved.Html);
    }

    public void Dispose() { services.Dispose(); Directory.Delete(directory, recursive: true); }
    private sealed class ImageHandler(bool fail = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            var response = new HttpResponseMessage(fail ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")) };
            response.Content.Headers.ContentType = new("image/png");
            return Task.FromResult(response);
        }
    }
}
