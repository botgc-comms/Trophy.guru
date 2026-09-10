using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Configuration;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class ProductDiscoveryTests
{
    [Fact]
    public void StoredBlogBodyCannotIntroduceASecondPageHeading()
    {
        var post = new Trophy.Catalogue.Domain.BlogPost(new() { Title = "Trophy guide", Slug = "trophy-guide", MetaDescription = "Guide description" }, "<h1>Imported title</h1><p>Article text</p>", null, null);
        var doc = new HtmlParser().ParseDocument(BlogPages.Article(post, "https://trophy.guru", "test-nonce"));
        Assert.Single(doc.QuerySelectorAll("h1"));
        Assert.Contains("Imported title", doc.QuerySelector(".article-body h2")!.TextContent);
    }

    [Fact]
    public void EveryGuideHasCompleteReadableMetadataAndConsistentFaqSchema()
    {
        var parser = new HtmlParser();
        Assert.Equal(ProductPages.All.Length, ProductPages.All.Select(p => p.Title).Distinct().Count());
        foreach (var page in ProductPages.All)
        {
            var document = parser.ParseDocument(ProductPages.Render(page, "https://trophy.guru"));
            Assert.Single(document.QuerySelectorAll("h1"));
            Assert.Equal(page.Heading, document.QuerySelector("h1")!.TextContent);
            Assert.Equal(page.Title, document.Title);
            Assert.Equal("https://trophy.guru" + page.Path, document.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
            Assert.Equal(page.Description, document.QuerySelector("meta[name=description]")!.GetAttribute("content"));
            Assert.DoesNotContain("noindex", document.QuerySelector("meta[name=robots]")!.GetAttribute("content")!);
            foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
            {
                using var json = JsonDocument.Parse(script.TextContent);
                Assert.True(json.RootElement.TryGetProperty("@type", out var type));
                if (type.GetString() == "FAQPage")
                    foreach (var faq in json.RootElement.GetProperty("mainEntity").EnumerateArray())
                    {
                        Assert.Contains(faq.GetProperty("name").GetString()!, document.Body!.TextContent);
                        Assert.Contains(faq.GetProperty("acceptedAnswer").GetProperty("text").GetString()!, document.Body!.TextContent);
                    }
            }
        }
    }

    [Fact]
    public void EntityGraphUsesTheSameProducerAndSoftwareIds()
    {
        using var json = JsonDocument.Parse(ProductPages.Graph("https://trophy.guru"));
        var graph = json.RootElement.GetProperty("@graph").EnumerateArray().ToArray();
        Assert.Equal(4, graph.Length);
        Assert.Contains(graph, e => e.GetProperty("@id").GetString() == "https://trophy.guru/#organization" && e.GetProperty("name").GetString() == "Marabou Stork Limited");
        Assert.Contains(graph, e => e.GetProperty("@id").GetString() == "https://trophy.guru/#software");
        Assert.DoesNotContain("AggregateRating", ProductPages.Graph("https://trophy.guru"));
    }

    [Theory]
    [InlineData("/archive.html")]
    [InlineData("/account-security.html")]
    [InlineData("/api/public/clubs/example/honours")]
    [InlineData("/honours/example")]
    [InlineData("/embed/example")]
    [InlineData("/mcp")]
    [InlineData("/blog/../archive.html")]
    public void IndexNowRejectsPrivateOrUtilityPaths(string path) => Assert.False(IndexNowPublisher.PublicPath(path));

    [Theory]
    [InlineData("../not-a-key")]
    [InlineData("tiny")]
    [InlineData("<script>bad</script>")]
    public void IndexNowRejectsUnsafeKeys(string key) => Assert.Null(IndexNowPublisher.Key(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["INDEXNOW_KEY"] = key }).Build()));
}
