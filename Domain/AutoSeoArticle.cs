using System.Text.Json.Serialization;

namespace Trophy.Catalogue.Domain;

public sealed record AutoSeoArticle
{
    public string Event { get; init; } = "";
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string Slug { get; init; } = "";
    [JsonPropertyName("published_url")] public string? PublishedUrl { get; init; }
    public string MetaDescription { get; init; } = "";
    [JsonPropertyName("content_html")] public string ContentHtml { get; init; } = "";
    [JsonPropertyName("content_markdown")] public string? ContentMarkdown { get; init; }
    public string? HeroImageUrl { get; init; }
    public string? HeroImageAlt { get; init; }
    public string? InfographicImageUrl { get; init; }
    public string[]? Keywords { get; init; }
    public string? MetaKeywords { get; init; }
    public BlogFaq[]? FaqSchema { get; init; }
    public string LanguageCode { get; init; } = "en";
    public long? SourceArticleId { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record BlogFaq(string Question, string Answer);
public sealed record BlogPost(AutoSeoArticle Article, string Html, string? HeroPath, string? InfographicPath);
