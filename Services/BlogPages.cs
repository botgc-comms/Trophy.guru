using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class BlogPages
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string PathFor(BlogPost post) => "/blog/" + Uri.EscapeDataString(post.Article.Slug);
    private static string Date(DateTimeOffset date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    private static string Image(string? path, string? alt, string css = "") => path is null ? "" :
        $"<img class=\"{css}\" src=\"{E(path)}\" alt=\"{E(alt)}\" loading=\"lazy\">";

    public static string Index(IReadOnlyList<BlogPost> posts, string origin, int page)
    {
        var cards = string.Join("", posts.Take(12).Select(post => $"""
            <article class="blog-card"><a href="{E(PathFor(post))}">
            <div class="card-copy"><p class="eyebrow">{E(Date(post.Article.PublishedAt))}</p>
            <h2>{E(post.Article.Title)}</h2><p>{E(post.Article.MetaDescription)}</p><span class="read-link">Read article <span aria-hidden="true">&rarr;</span></span></div></a></article>
            """));
        if (posts.Count == 0) cards = "<div class=\"blog-empty\"><h2>More stories to come</h2><p>We’re preparing practical guides to help you preserve your club’s sporting history. Check back soon.</p><a href=\"/uk/how-to-catalogue-trophy-winners/\">Explore our trophy guide &rarr;</a></div>";
        var pagination = "<nav class=\"pagination\" aria-label=\"Blog pages\">" +
            (page > 1 ? $"<a href=\"/blog?page={page - 1}\">&larr; Newer articles</a>" : "") +
            (posts.Count > 12 ? $"<a href=\"/blog?page={page + 1}\">Older articles &rarr;</a>" : "") + "</nav>";
        return Layout(page > 1 ? $"The Trophy Guru blog — Page {page}" : "The Trophy Guru blog", "Ideas and practical guides for preserving trophies, sporting memories and club history.",
            origin + "/blog" + (page > 1 ? "?page=" + page : ""), "en", "", $"""
            <section class="blog-intro"><p class="eyebrow">The Trophy Guru journal</p><h1>Every trophy has a story.</h1>
            <p>Ideas, stories and practical guides to help you preserve the names, memories and achievements that make your club.</p></section>
            <section class="blog-grid" aria-label="Latest articles">{cards}</section>{pagination}
            <section class="blog-reading" aria-labelledby="archive-guides-title"><h2 id="archive-guides-title">Build an archive your club can trust</h2>
            <p>A trophy collection holds more than a list of winners. Names, dates and competition titles connect today's members with earlier generations. These guides help you work through that evidence, from taking usable photographs to checking a digital record before sharing it. Choose the subject that matches your next step, and keep the original trophy or document available as you work.</p>
            <h3>Start with clear photographs</h3><p>Photograph the whole trophy first, then take closer views of each engraved area. Include overlapping views where lettering curves around a cup or base. Reflections can conceal a letter or date, so compare several angles before deciding what an inscription says. Our <a href="/uk/how-to-catalogue-trophy-winners/">UK trophy catalogue guide</a> and <a href="/us/how-to-catalog-trophy-winners/">US trophy catalog guide</a> explain how to organise the records for your club.</p>
            <h3>Review the names before sharing</h3><p>An automatic reading is a starting point for checking the evidence. Compare proposed winners with the photographs and other club records, especially where initials, worn lettering or missing years leave room for doubt. Preserve unresolved gaps until you have a reliable source. The <a href="/how-it-works">Trophy Guru workflow</a> explains how photographs become reviewed records, and the <a href="/electronic-honours-boards">electronic honours board guide</a> covers presenting confirmed history to members.</p></section>
            """);
    }

    public static string Article(BlogPost post, string origin, string nonce, IReadOnlyList<BlogPost>? related = null)
    {
        var a = post.Article;
        var url = origin + PathFor(post);
        var schema = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "BlogPosting", ["headline"] = a.Title,
            ["description"] = a.MetaDescription, ["url"] = url, ["mainEntityOfPage"] = url,
            ["datePublished"] = a.PublishedAt, ["dateModified"] = a.UpdatedAt,
            ["inLanguage"] = a.LanguageCode, ["keywords"] = a.Keywords,
            ["image"] = post.HeroPath is null ? null : origin + post.HeroPath,
            ["publisher"] = new Dictionary<string, object> { ["@type"] = "Organization", ["@id"] = origin + "/#organization", ["name"] = "Marabou Stork Limited", ["url"] = origin + "/about" }
        };
        var metadata = $"<meta property=\"og:type\" content=\"article\"><meta name=\"keywords\" content=\"{E(a.MetaKeywords ?? string.Join(", ", a.Keywords ?? []))}\">" +
            (post.HeroPath is null ? "" : $"<meta property=\"og:image\" content=\"{E(origin + post.HeroPath)}\"><meta property=\"og:image:alt\" content=\"{E(a.HeroImageAlt)}\">") +
            $"<script type=\"application/ld+json\" nonce=\"{E(nonce)}\">{JsonSerializer.Serialize(schema)}</script>";
        var faqs = "";
        var bodyDocument = new HtmlParser().ParseDocument(post.Html);
        var validFaqs = (a.FaqSchema ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Question) && !string.IsNullOrWhiteSpace(f.Answer))
            .DistinctBy(f => Normalize(f.Question), StringComparer.OrdinalIgnoreCase)
            .Select(f => {
                var heading = bodyDocument.QuerySelectorAll("h2,h3,h4,h5,h6,summary")
                    .FirstOrDefault(h => string.Equals(Normalize(h.TextContent), Normalize(f.Question), StringComparison.OrdinalIgnoreCase));
                // Use the visible answer when the supplied body already contains this FAQ.
                // A heading alone does not prove the answer has been rendered.
                var answer = heading?.NextElementSibling;
                var visibleAnswer = answer is not null && answer.LocalName is "p" or "div" or "ul" or "ol"
                    ? Normalize(answer.TextContent) : "";
                return (Faq: visibleAnswer.Length > 0 ? new BlogFaq(f.Question, visibleAnswer) : f, InBody: visibleAnswer.Length > 0);
            }).ToArray();
        if (validFaqs.Length > 0)
        {
            var faqSchema = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org", ["@type"] = "FAQPage",
                ["mainEntity"] = validFaqs.Select(f => new Dictionary<string, object>
                {
                    ["@type"] = "Question", ["name"] = f.Faq.Question,
                    ["acceptedAnswer"] = new Dictionary<string, object> { ["@type"] = "Answer", ["text"] = f.Faq.Answer }
                }).ToArray()
            };
            metadata += $"<script type=\"application/ld+json\" nonce=\"{E(nonce)}\">{JsonSerializer.Serialize(faqSchema)}</script>";
            var missingFaqs = validFaqs.Where(f => !f.InBody).ToArray();
            if (missingFaqs.Length > 0)
                faqs = "<section class=\"blog-faq\"><h2>Frequently asked questions</h2>" + string.Join("", missingFaqs.Select(f => $"<h3>{E(f.Faq.Question)}</h3><p>{E(f.Faq.Answer)}</p>")) + "</section>";
        }
        // Images already embedded in the supplied body are not repeated.
        var hero = post.HeroPath is not null && post.Html.Contains(post.HeroPath, StringComparison.Ordinal) ? "" : Image(post.HeroPath, a.HeroImageAlt, "article-hero");
        var infographic = post.InfographicPath is not null && (post.InfographicPath == post.HeroPath || post.Html.Contains(post.InfographicPath, StringComparison.Ordinal)) ? "" : Image(post.InfographicPath, "Infographic: " + a.Title, "article-infographic");
        return Layout(a.Title, a.MetaDescription, url, a.LanguageCode, metadata, $"""
            <article class="blog-article"><header class="article-heading"><a href="/blog" class="back-link">&larr; All articles</a>
            <p class="eyebrow">Trophy Guru journal &middot; <time datetime="{a.PublishedAt:O}">{E(Date(a.PublishedAt))}</time></p>
            <h1>{E(a.Title)}</h1><p class="article-summary">{E(a.MetaDescription)}</p></header>
            {hero}<div class="article-body">{System.Text.RegularExpressions.Regex.Replace(post.Html, @"<(\/?)h1(\s|>)", "<$1h2$2", System.Text.RegularExpressions.RegexOptions.IgnoreCase)}{infographic}{faqs}</div></article>{RelatedArticles(related ?? [])}
            """);
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static string RelatedArticles(IReadOnlyList<BlogPost> posts) => posts.Count == 0 ? "" :
        "<aside class=\"blog-reading\" aria-labelledby=\"related-title\"><h2 id=\"related-title\">More guides for your club archive</h2><ul>" +
        string.Join("", posts.Select(p => $"<li><a href=\"{E(PathFor(p))}\">{E(p.Article.Title)}</a></li>")) + "</ul></aside>";

    // Keep full editorial titles in headings and article schema. Search titles
    // retain the brand while avoiding both long titles and H1/title duplicates.
    public static string SearchTitle(string title)
    {
        const string suffix = " | Trophy Guru";
        const int limit = 70 - 14;
        title = title.Trim();
        if (title.Length <= limit) return title + suffix;
        var subtitle = title.IndexOf(':');
        if (subtitle is >= 20 and <= limit) return title[..subtitle].TrimEnd() + suffix;
        var shortened = title[..(limit - 1)];
        var boundary = shortened.LastIndexOf(' ');
        if (boundary >= 30) shortened = shortened[..boundary];
        if (char.IsHighSurrogate(shortened[^1])) shortened = shortened[..^1];
        return shortened.TrimEnd(' ', ':', ';', ',', '-', '|') + "…" + suffix;
    }

    private static string Layout(string title, string description, string url, string language, string metadata, string body) => $"""
        <!doctype html><html lang="{E(language)}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{E(SearchTitle(title))}</title><meta name="description" content="{E(description)}"><link rel="canonical" href="{E(url)}">
        <meta name="robots" content="index,follow,max-image-preview:large"><meta property="og:site_name" content="Trophy Guru">{(metadata.Contains("property=\"og:image\"") ? "" : $"<meta property=\"og:image\" content=\"{E(new Uri(new Uri(url), "/marketing/trophy-guru-social.png").AbsoluteUri)}\">")}
        <meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{E(title)}"><meta name="twitter:description" content="{E(description)}"><meta name="twitter:image" content="{E(new Uri(new Uri(url), "/marketing/trophy-guru-social.png").AbsoluteUri)}">
        <link rel="stylesheet" href="/analytics.css"><script src="/analytics.js" defer></script><script src="/webmcp.js" defer></script>
        <meta name="theme-color" content="#061711"><meta property="og:title" content="{E(title)}"><meta property="og:description" content="{E(description)}"><meta property="og:url" content="{E(url)}">
        <link rel="icon" href="/favicon.svg" type="image/svg+xml"><link rel="stylesheet" href="/blog.css">{metadata}<link rel="stylesheet" href="/branding.css"></head>
        <body><a class="skip-link" href="#main">Skip to content</a><header class="blog-header"><a href="/" aria-label="Trophy Guru home"><img src="/images/brand/trophy-guru-logo-transparent.webp" width="240" height="64" alt="Trophy Guru"></a>
        <nav aria-label="Main navigation"><a href="/">Home</a><a href="/blog" aria-current="page">Blog</a><a class="button" href="/archive.html#signup">Start your archive</a></nav></header>
        <main id="main">{body}<aside class="blog-cta"><p class="eyebrow">Preserve every name. Every year.</p><h2>Your club’s history deserves to be remembered.</h2><p>Turn your trophy inscriptions into a searchable archive, one photograph at a time.</p><a class="button" href="/archive.html#signup">Try your first trophy free &rarr;</a><p><a class="demo-link" href="/demo">Explore the public honours board demo — no account needed</a></p></aside></main>
        {SiteBranding.Footer}</body></html>
        """;
}
