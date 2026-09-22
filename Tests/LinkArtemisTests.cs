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
    private LinkArtemisSync Sync(Handler? handler = null, IConfiguration? settings = null, BlogImages? images = null) => new(settings ?? config, new Factory(handler ?? new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") })), store, NullLogger<LinkArtemisSync>.Instance, images ?? Images());
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
        var restarted = new LinkArtemisSync(config, new Factory(new Handler(_ => throw new Exception())), new BlogStore(directory), NullLogger<LinkArtemisSync>.Instance, Images());
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
    private static readonly byte[] CoverPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aSrsAAAAASUVORK5CYII=");
    private static BlogImages Images(Func<HttpRequestMessage,HttpResponseMessage>? reply = null) => new(new HttpClient(new Handler(reply ?? (_ => new(HttpStatusCode.NotFound)))));
    private static HttpResponseMessage CoverResponse() { var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CoverPng) }; r.Content.Headers.ContentType = new("image/png"); return r; }
    [Fact]
    public async Task ImportsCoverLocallyWithDimensionsAndKeepsListingTextOnly()
    {
        var source = Article() with { HeroImageUrl = "https://images.example/cover.png" };
        var sync = Sync(images:Images(request => { Assert.False(request.Headers.Contains("X-API-Key")); return CoverResponse(); }));
        Assert.True(await sync.ImportAsync(source, default));
        var post = store.List()[0];
        Assert.StartsWith("/blog/images/",post.HeroPath);
        Assert.Equal(1,post.Article.HeroImageWidth); Assert.Equal(1,post.Article.HeroImageHeight);
        var html = BlogPages.Article(post,"https://trophy.guru","test");
        Assert.Contains("class=\"article-hero\"",html); Assert.Contains("width=\"1\" height=\"1\"",html);
        Assert.DoesNotContain(post.HeroPath!, BlogPages.Index(store.List(),"https://trophy.guru",1));
        Assert.Contains("og:image",html);
    }
    [Fact]
    public async Task PollRepairsMissingCoverWithoutReplacingReviewedTextOrDuplicatingPosts()
    {
        var source = Article(); Assert.True(await Sync().ImportAsync(source,default));
        var old = store.List()[0];
        store.Upsert(old with { Html = "<p>Club-reviewed text must survive.</p>", Article = old.Article with { Title="Reviewed title", UpdatedAt=old.Article.UpdatedAt.AddSeconds(1) } });
        source = source with { HeroImageUrl="https://images.example/cover.png",ContentHtml="<p>Different upstream text.</p>" };
        var imageRequests = 0;
        var sync = Sync(new Handler(r => { Assert.Contains("?limit=",r.RequestUri!.ToString()); return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] {source},BlogStore.Json)) }; }), images:Images(_ => { imageRequests++;return CoverResponse(); }));
        await sync.RunOnceAsync(default);
        var post=store.List()[0];
        Assert.Equal("<p>Club-reviewed text must survive.</p>",post.Html);
        Assert.Equal("Reviewed title",post.Article.Title); Assert.Equal(old.Article.PublishedAt,post.Article.PublishedAt); Assert.Equal(old.Article.Slug,post.Article.Slug);
        Assert.Equal(1,sync.Status.ImagesUpdated); Assert.Equal(0,sync.Status.Imported); Assert.Single(store.List());
        await sync.RunOnceAsync(default); Assert.Equal(1,imageRequests); Assert.Equal(0,sync.Status.ImagesUpdated);
    }
    [Fact]
    public async Task FailedCoverDoesNotLoseArticleAndCanBeRetried()
    {
        var source=Article() with { HeroImageUrl="https://images.example/cover.png" };
        Assert.True(await Sync().ImportAsync(source,default));
        Assert.Null(store.List()[0].HeroPath);
        Assert.True(await Sync(images:Images(_ => CoverResponse())).RestoreCoverAsync(source,default));
        Assert.NotNull(store.List()[0].HeroPath); Assert.Single(store.List());
        var other=Article() with { Slug="other-article", HeroImageUrl="https://127.0.0.1/private.png" };
        Assert.True(await Sync(images:Images(_ => throw new Exception("Private URLs must not be fetched"))).ImportAsync(other,default));
        Assert.Null(store.Find(other.Slug!)!.HeroPath);
    }
    [Fact]
    public void ImageDimensionsHandleHeadersAndTruncatedData()
    {
        Assert.Equal((1,1),BlogImages.Dimensions(CoverPng));
        Assert.Equal((320,200),BlogImages.Dimensions(new byte[] {71,73,70,56,57,97,64,1,200,0}));
        Assert.Equal((640,480),BlogImages.Dimensions(new byte[] {255,216,255,192,0,8,8,1,224,2,128,0}));
        var webp = new byte[30]; Encoding.ASCII.GetBytes("RIFF").CopyTo(webp,0); Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(webp,8);webp[24]=127;webp[25]=2;webp[27]=223;webp[28]=1;
        Assert.Equal((640,480),BlogImages.Dimensions(webp));
        for(var i=0;i<24;i++) Assert.Null(BlogImages.Dimensions(CoverPng.Take(i).ToArray()));
        Assert.Null(BlogImages.Dimensions(new byte[] {255,216,255,192,255,255}));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    private sealed class Factory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler:false); }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => Task.FromResult(reply(request)); }
}
