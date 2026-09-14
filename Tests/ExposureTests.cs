using System.Text.Json;
using Microsoft.Extensions.Configuration;
using AngleSharp.Html.Parser;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class ExposureTests
{
    [Fact]
    public void ExistingFaqUsesVisibleAnswerWithoutDuplicatingTheQuestion()
    {
        var post = new BlogPost(new() { Slug = "faq-example", Title = "FAQ example", FaqSchema = [new("Can we export?", "An older answer."), new("Can we export?", "Duplicate."), new("Is review needed?", "Yes.")] },
            "<h2>Can we <em>export?</em></h2><p>The visible, current answer.</p>", null, null);
        var doc = new HtmlParser().ParseDocument(BlogPages.Article(post, "https://trophy.guru", "test"));
        Assert.Single(doc.QuerySelectorAll("h2,h3"), h => h.TextContent == "Can we export?");
        Assert.Contains("Is review needed?", doc.QuerySelector(".blog-faq")!.TextContent);
        Assert.DoesNotContain("An older answer.", doc.Body!.TextContent);
        var graph = doc.QuerySelectorAll("script[type='application/ld+json']").Select(e => JsonDocument.Parse(e.TextContent)).Single(d => d.RootElement.GetProperty("@type").GetString() == "FAQPage");
        Assert.Equal(2, graph.RootElement.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal("The visible, current answer.", graph.RootElement.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
    }

    [Fact]
    public void EditorialRevisionIsBackedUpIdempotentAndDoesNotReplaceNewerCmsContent()
    {
        var path = Path.Combine(Path.GetTempPath(), "trophy-editorial-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BlogStore(path);
            var revisions = BlogEditorialRevisions.Load();
            Assert.Equal(7, revisions.Count);
            var revision = revisions[0];
            var original = new BlogPost(new() { Id = 101, Slug = revision.Slug, Title = "Original", UpdatedAt = revision.SupersedesUpdatedAt, PublishedAt = revision.SupersedesUpdatedAt.AddHours(-1) }, "<p>Original public body.</p>", null, null);
            store.Upsert(original);
            Assert.Equal(1, BlogEditorialRevisions.Apply(store));
            var updated = store.Find(101)!;
            Assert.Equal(revision.Html, updated.Html);
            Assert.Equal(revision.Title, updated.Article.Title);
            Assert.Equal(original.Article.PublishedAt, updated.Article.PublishedAt);
            Assert.Equal(original.Article.Slug, updated.Article.Slug);
            Assert.Empty(updated.Article.FaqSchema!);
            Assert.Equal(0, BlogEditorialRevisions.Apply(store));
            var backup = Assert.Single(Directory.GetFiles(Path.Combine(path, "editorial-backups")));
            Assert.Equal(original.Html, JsonSerializer.Deserialize<BlogPost>(File.ReadAllText(backup), BlogStore.Json)!.Html);
            var later = original with { Article = original.Article with { Title = "Later CMS version", UpdatedAt = BlogEditorialRevisions.PublishedAt.AddDays(1) }, Html = "<p>New editorial decision.</p>" };
            store.Upsert(later);
            Assert.Equal(0, BlogEditorialRevisions.Apply(store));
            Assert.Equal(later.Html, store.Find(101)!.Html);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void IndexNowProductionDefaultsSurviveEmptyHostingPlaceholdersButRespectExplicitDisable()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["PUBLIC_SITE_URL"] = "https://trophy.guru", ["INDEXNOW_KEY"] = "", ["INDEXNOW_ENABLED"] = ""
        }).Build();
        Assert.Equal(IndexNowPublisher.DefaultPublicKey, IndexNowPublisher.Key(config));
        Assert.True(IndexNowPublisher.Enabled(config));
        config["INDEXNOW_ENABLED"] = "false";
        Assert.False(IndexNowPublisher.Enabled(config));
        config["INDEXNOW_KEY"] = "invalid/key";
        Assert.Null(IndexNowPublisher.Key(config));
        config["PUBLIC_SITE_URL"] = "https://example.test";
        config["INDEXNOW_KEY"] = "";
        config["INDEXNOW_ENABLED"] = "";
        Assert.False(IndexNowPublisher.Enabled(config));
        Assert.Null(IndexNowPublisher.Key(config));
    }

    [Fact]
    public void DemoIsPublicWhileCustomerArchivesRemainOutsideIndexNowAllowList()
    {
        Assert.True(IndexNowPublisher.PublicPath("/demo"));
        Assert.True(IndexNowPublisher.PublicPath("/trophy-archive-project-plan"));
        Assert.False(IndexNowPublisher.PublicPath("/honours.html"));
        Assert.False(IndexNowPublisher.PublicPath("/archive.html"));
        Assert.False(IndexNowPublisher.PublicPath("/api/trophies"));
        var page = ProductPages.All.Single(p => p.Path == "/demo");
        var doc = new HtmlParser().ParseDocument(ProductPages.Render(page, "https://trophy.guru"));
        Assert.NotNull(doc.QuerySelector("a[href='/honours.html?demo=1']"));
        Assert.NotNull(doc.QuerySelector("a[href='/archive.html#signup']"));
        Assert.Contains("fictional", doc.QuerySelector("main")!.TextContent);
        Assert.NotNull(doc.QuerySelector("img[src='/marketing/honours-board-example.webp']"));
    }
}
