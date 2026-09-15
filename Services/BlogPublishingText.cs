using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Markdig;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

// The Zapier New Copy Published trigger can supply just one text field.
internal static class BlogPublishingText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static BlogPublishRequest Normalize(BlogPublishRequest request)
    {
        var text = request.Text!.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        // Markdown preserves embedded HTML; the existing publication sanitizer still applies.
        using var document = new HtmlParser().ParseDocument(Markdown.ToHtml(text, Pipeline));
        foreach (var element in document.QuerySelectorAll("script,style,nav,header,footer,form,iframe").ToArray())
            element.Remove();
        var body = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body!;
        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            var heading = body.QuerySelector("h1") ?? document.QuerySelector("title") ?? body.QuerySelector("h2");
            if (heading is not null)
            {
                title = Regex.Replace(heading.TextContent, @"\s+", " ").Trim();
                heading.Remove();
            }
            else
            {
                // Plain-text exports conventionally put the article title on the first line.
                var first = body.QuerySelector("p");
                var firstLine = first?.TextContent.Split('\n', 2)[0].Trim() ?? "";
                if (firstLine.Length is > 0 and <= 200)
                {
                    title = firstLine;
                    var offset = first!.TextContent.IndexOf(firstLine, StringComparison.Ordinal) + firstLine.Length;
                    var remainder = first.TextContent[offset..].Trim();
                    if (remainder.Length == 0) first.Remove();
                    else first.TextContent = remainder;
                }
            }
        }
        var automaticIdentity = string.IsNullOrWhiteSpace(request.DocumentId);
        // Content identity makes repeat deliveries harmless; a changed export cannot overwrite another post.
        var identity = automaticIdentity
            ? "text-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            : request.DocumentId;
        return request with
        {
            Title = title, ContentHtml = body.InnerHtml, DocumentId = identity,
            UpdatedAt = automaticIdentity ? DateTimeOffset.UtcNow : request.UpdatedAt
        };
    }
}
