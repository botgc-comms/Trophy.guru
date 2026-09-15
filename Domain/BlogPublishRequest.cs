namespace Trophy.Catalogue.Domain;

// Our receiving contract, mapped from the actual Writesonic trigger fields in Zapier.
public sealed record BlogPublishRequest
{
    public string Mode { get; init; } = "validate";
    public string DocumentId { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
    public string Title { get; init; } = "";
    public string? Slug { get; init; }
    public string ContentHtml { get; init; } = "";
    public string? MetaDescription { get; init; }
    public string? HeroImageUrl { get; init; }
    public string? HeroImageAlt { get; init; }
}
