using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class SeoAuditTests
{
    [Theory]
    [InlineData("Community Club Trophy Records Digitization: The Complete 2026 Guide")]
    [InlineData("Automatic Trophy Data Entry: Modernising Your Club Heritage in 2026")]
    [InlineData("Photographing Trophies for AI Transcription (2026 Guide)")]
    [InlineData("Transcribe Names from Silver Trophies: A Step-by-Step Guide")]
    [InlineData("AI Trophy Engraving Transcription Service: Modernise Your Club Heritage")]
    [InlineData("A guide with an unusually long title about preserving the history of your club collection and its winners")]
    public void ArticleSearchTitlesStayConciseWithoutChangingEditorialHeadings(string title)
    {
        var post = new BlogPost(new() { Title = title, Slug = "test-guide" }, "<p>Verified source material.</p>", null, null);
        var doc = new HtmlParser().ParseDocument(BlogPages.Article(post, "https://trophy.guru", "test-nonce"));
        Assert.NotNull(doc.Title);
        Assert.InRange(doc.Title.Length, 15, 70);
        Assert.EndsWith(" | Trophy Guru", doc.Title);
        Assert.NotEqual(title, doc.Title);
        Assert.Equal(title, doc.QuerySelector("h1")!.TextContent);
        using var json = JsonDocument.Parse(doc.QuerySelector("script[type='application/ld+json']")!.TextContent);
        Assert.Equal(title, json.RootElement.GetProperty("headline").GetString());
    }

    [Fact]
    public void EmptyBlogStillProvidesUsefulReadableGuidance()
    {
        var doc = new HtmlParser().ParseDocument(BlogPages.Index([], "https://trophy.guru", 1));
        var main = doc.QuerySelector("main")!;
        Assert.True(Regex.Matches(main.TextContent, @"\b[\p{L}\p{N}]+\b").Count >= 200);
        Assert.NotNull(main.QuerySelector("a[href='/uk/how-to-catalogue-trophy-winners/']"));
        Assert.NotNull(main.QuerySelector("a[href='/us/how-to-catalog-trophy-winners/']"));
    }

    [Fact]
    public void OlderArticlesReceiveLinksEvenAfterTheyLeaveTheFirstBlogPage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trophy-seo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BlogStore(directory);
            for (var i = 0; i < 25; i++)
                store.Upsert(new(new() { Id = i, Slug = $"guide-{i:00}", Title = $"Guide {i}", PublishedAt = DateTimeOffset.UtcNow.AddDays(-i) }, "<p>Guide.</p>", null, null));
            var posts = store.List();
            var incoming = posts.ToDictionary(p => p.Article.Slug, _ => 0);
            foreach (var post in posts)
            {
                var related = store.Related(post.Article.Slug);
                Assert.Equal(3, related.Count);
                Assert.DoesNotContain(related, p => p.Article.Slug == post.Article.Slug);
                var doc = new HtmlParser().ParseDocument(BlogPages.Article(post, "https://trophy.guru", "test-nonce", related));
                foreach (var link in doc.QuerySelectorAll(".blog-reading a"))
                    incoming[link.GetAttribute("href")!["/blog/".Length..]]++;
            }
            Assert.All(incoming.Values, count => Assert.Equal(3, count));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RelatedArticlesHandleEmptyAndSingleArticleCollections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trophy-seo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BlogStore(directory);
            Assert.Empty(store.Related("only-guide"));
            store.Upsert(new(new() { Id = 1, Slug = "only-guide", Title = "The only guide" }, "<p>Guide.</p>", null, null));
            Assert.Empty(store.Related("only-guide"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
