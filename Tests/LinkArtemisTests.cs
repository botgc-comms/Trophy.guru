using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Trophy.Catalogue.Services;
using Trophy.Catalogue.Domain;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class LinkArtemisTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trophy-latitude-" + Guid.NewGuid().ToString("N"));
    private readonly BlogStore store;
    private readonly IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["LINKARTEMIS_API_KEY"] = "test-not-a-real-key", ["LINKARTEMIS_ENABLED"] = "true"
    }).Build();
    public LinkArtemisTests() => store = new(directory);
    private static LinkArtemisSync.Article Article(string? id = null) => new(id ?? Guid.NewGuid().ToString(), "Club trophy records", "club-trophy-records", null, null,
        "<h1>Club trophy records</h1><p>Photograph each trophy and check the names against the original club records. Keep source photos available so future club volunteers can review uncertain entries.</p>", null, "UK", DateTimeOffset.UtcNow.AddDays(-1));
    private LinkArtemisSync Sync(Handler? handler = null, IConfiguration? settings = null) => new(settings ?? config, new Factory(handler ?? new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") })), store, NullLogger<LinkArtemisSync>.Instance);
    [Fact]
    public async Task ImportsSanitizedTextUsingExistingTemplateAndKeepsEditorialEditsAcrossRestart()
    {
        var original = Article();
        var source = original with { ContentHtml = original.ContentHtml + "<script>evil()</script><style>body{display:none}</style><img src='https://bad.example/pic.jpg'><p onclick='evil()'>More history.</p>" };
        Assert.True(await Sync().ImportAsync(source, default));
        var post = store.Find(LinkArtemisSync.ArticleId(source.Id))!;
        Assert.DoesNotContain("script", post.Html); Assert.DoesNotContain("onclick", post.Html); Assert.DoesNotContain("<img", post.Html);
        Assert.DoesNotContain("<h1", post.Html); Assert.DoesNotContain("evil", post.Html);
        Assert.Null(post.HeroPath); Assert.Null(post.Article.HeroImageUrl);
        Assert.Equal("en-GB", post.Article.LanguageCode);
        Assert.Contains("BlogPosting", BlogPages.Article(post, "https://trophy.guru", "test"));
        var changed = post with { Html = "<p>Reviewed by the club.</p>", Article = post.Article with { UpdatedAt = post.Article.UpdatedAt.AddHours(1) } };
        store.Upsert(changed);
        var restarted = new LinkArtemisSync(config, new Factory(new Handler(_ => throw new Exception())), new BlogStore(directory), NullLogger<LinkArtemisSync>.Instance);
        Assert.False(await restarted.ImportAsync(source with { Title = "Provider changed title", Slug = "different-url" }, default));
        Assert.Equal(changed.Html, store.Find(post.Article.Id)!.Html);
        Assert.Equal(post.Article.Slug, store.Find(post.Article.Id)!.Article.Slug);
        Assert.Single(store.List());
    }
    [Fact]
    public async Task DifferentArticleCannotOverwriteExistingSlug()
    {
        Assert.True(await Sync().ImportAsync(Article(), default));
        Assert.False(await Sync().ImportAsync(Article(), default));
        Assert.Single(store.List());
    }
    [Fact]
    public async Task SupportsMarkdownAndRejectsMalformedOrWrongLanguageContent()
    {
        var a = Article();
        Assert.False(await Sync().ImportAsync(a with { Id = "../../invalid" }, default));
        Assert.False(await Sync().ImportAsync(a with { ContentHtml = "<p>Example.</p>" }, default));
        Assert.False(await Sync().ImportAsync(a with { LanguageCode = "FR" }, default));
        Assert.False(await Sync().ImportAsync(a with { Slug = "../../private" }, default));
        Assert.False(await Sync().ImportAsync(a with { CreatedAt = DateTimeOffset.UtcNow.AddDays(1) }, default));
        Assert.True(await Sync().ImportAsync(a with { ContentHtml = null, ContentMarkdown = "## Check the sources\n\nTake clear photos of your trophies and check every name against the source. Keep uncertain readings marked for review before sharing the winners online." }, default));
        Assert.Contains("<h2>", store.List()[0].Html);
    }
    [Fact]
    public async Task FetchesDetailsNotSummaryAndSkipsExistingOnNextPoll()
    {
        var article = Article();
        var calls = new List<string>();
        var handler = new Handler(request => {
            Assert.Equal("https://app.linkartemis.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Equal("test-not-a-real-key", request.Headers.GetValues("X-API-Key").Single());
            calls.Add(request.RequestUri.PathAndQuery);
            var data = request.RequestUri.Query.Length > 0 ? JsonSerializer.Serialize(new[] { article with { ContentHtml = null } }, BlogStore.Json) : JsonSerializer.Serialize(article, BlogStore.Json);
            return new(HttpStatusCode.OK) { Content = new StringContent(data) };
        });
        var sync = Sync(handler);
        await sync.RunOnceAsync(default);
        Assert.Single(store.List()); Assert.Equal(1, sync.Status.Imported);
        await sync.RunOnceAsync(default);
        Assert.Equal(3, calls.Count); Assert.Equal(0, sync.Status.Imported); Assert.Single(store.List());
    }
    [Fact]
    public async Task PaginationAndAllowlistFindAnOlderSelectedArticle()
    {
        var selected = Article(); config["LINKARTEMIS_ARTICLE_IDS"] = selected.Id;
        var unwanted = Enumerable.Range(0,100).Select(_ => Article()).ToArray();
        var calls = 0;
        var sync = Sync(new Handler(request => {
            calls++;
            object result = request.RequestUri!.Query.Contains("offset=0") ? unwanted : request.RequestUri.Query.Contains("offset=100") ? new[] { selected } : selected;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, BlogStore.Json)) };
        }));
        await sync.RunOnceAsync(default);
        Assert.Equal(3, calls); Assert.Single(store.List()); Assert.Equal(LinkArtemisSync.ArticleId(selected.Id), store.List()[0].Article.Id);
    }
    [Fact]
    public async Task RateLimitIsReportedWithRetryAfterAndNoPublication()
    {
        var sync = Sync(new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromMinutes(30)); return r; }));
        var error = await Assert.ThrowsAsync<LinkArtemisSync.ApiException>(() => sync.RunOnceAsync(default));
        Assert.Equal(429,error.Status); Assert.Equal(TimeSpan.FromMinutes(30), error.RetryAfter); Assert.Empty(store.List());
    }
    [Fact]
    public async Task RequiresExplicitEnableAndMismatchedDetailsAreNotPublished()
    {
        config["LINKARTEMIS_ENABLED"] = "false";
        await Sync(new Handler(_ => throw new Exception("Disabled integration must not call provider"))).RunOnceAsync(default);
        config["LINKARTEMIS_ENABLED"] = "true";
        var summary = Article(); var wrong = Article();
        var sync = Sync(new Handler(r => new(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.Query.Length > 0 ? JsonSerializer.Serialize(new[] { summary }, BlogStore.Json) : JsonSerializer.Serialize(wrong, BlogStore.Json)) }));
        await sync.RunOnceAsync(default);
        Assert.Empty(store.List()); Assert.Equal(1,sync.Status.Skipped);
    }
    [Fact]
    public async Task ConcurrentImportsPublishOnlyOneCopy()
    {
        var source = Article(); var sync = Sync();
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => sync.ImportAsync(source,default)));
        Assert.Single(results, r => r); Assert.Single(store.List());
        Assert.NotEqual(BlogPublishingEndpoints.ArticleId(source.Id),LinkArtemisSync.ArticleId(source.Id));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    private sealed class Factory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler:false); }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => Task.FromResult(reply(request)); }
}
