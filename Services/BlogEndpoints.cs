using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class BlogEndpoints
{
    public const string WebhookPath = "/api/webhooks/autoseo";
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    public static void MapBlog(this WebApplication app)
    {
        app.MapPost(WebhookPath, ReceiveAsync).WithMetadata(new RequestBodyLimit(MaxBodyBytes));
        app.MapGet("/blog", (HttpContext context, BlogStore store, IConfiguration config) =>
        {
            var page = int.TryParse(context.Request.Query["page"], out var parsed) ? Math.Clamp(parsed, 1, 100000) : 1;
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Content(BlogPages.Index(store.List(13, (page - 1) * 12), PublicOrigin(config), page), "text/html; charset=utf-8");
        });
        app.MapGet("/blog/{slug}", (string slug, HttpContext context, BlogStore store, IConfiguration config) =>
        {
            var post = store.Find(slug);
            if (post is null) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Content(BlogPages.Article(post, PublicOrigin(config), context.Items["csp-nonce"]?.ToString() ?? ""), "text/html; charset=utf-8");
        });
        app.MapGet("/blog/images/{name}", (string name, BlogStore store) =>
        {
            if (!Regex.IsMatch(name, @"\A[a-f0-9]{64}\.(png|jpg|webp|gif)\z")) return Results.NotFound();
            var path = Path.Combine(store.ImageDirectory, name);
            if (!File.Exists(path)) return Results.NotFound();
            var type = Path.GetExtension(name) switch { ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => "image/gif" };
            return Results.File(path, type);
        });
    }

    public static string PublicOrigin(IConfiguration config)
    {
        var candidate = config["PUBLIC_SITE_URL"];
        if (string.IsNullOrWhiteSpace(candidate)) candidate = config["RENDER_EXTERNAL_URL"];
        if (string.IsNullOrWhiteSpace(candidate)) candidate = "https://trophy.guru";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Set PUBLIC_SITE_URL to the public HTTPS origin.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static bool Authorized(string authorization, string token) =>
        !string.IsNullOrWhiteSpace(token) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(authorization)),
            SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token)));

    public static bool ValidSignature(byte[] rawBody, string signature, string token)
    {
        if (signature.Length != 64) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), rawBody));
        }
        catch (FormatException) { return false; }
    }

    public static async Task<IResult> ReceiveAsync(HttpContext context, BlogStore store, BlogImages images,
        IConfiguration config, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AutoSeoWebhook");
        var token = config["AUTOSEO_WEBHOOK_TOKEN"] ?? "";
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { error = "webhook_not_configured" }, statusCode: 503);
        if (!Authorized(context.Request.Headers.Authorization.ToString(), token)) return Results.Unauthorized();
        try
        {
            var origin = PublicOrigin(config);
            if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "expected_json" }, statusCode: 415);
            var rawBody = await BlogImages.ReadBoundedAsync(context.Request.Body, MaxBodyBytes, context.RequestAborted);
            var signature = context.Request.Headers["X-AutoSEO-Signature"].ToString();
            if ((context.Request.Headers.ContainsKey("X-AutoSEO-Signature") || config.GetValue<bool>("AUTOSEO_REQUIRE_SIGNATURE")) &&
                !ValidSignature(rawBody, signature, token)) return Results.Unauthorized();
            var article = JsonSerializer.Deserialize<AutoSeoArticle>(rawBody, BlogStore.Json);
            if (article is null) return Results.BadRequest(new { error = "invalid_article" });
            var headerEvent = context.Request.Headers["X-AutoSEO-Event"].ToString();
            if (headerEvent.Length != 0 && headerEvent != article.Event) return Results.BadRequest(new { error = "event_mismatch" });
            if (article.Event == "test") return Results.Ok(new { url = origin + "/test" });
            if (article.Event is not ("article.published" or "article.updated"))
                return Results.BadRequest(new { error = "unsupported_event" });
            if (article.Id <= 0 || string.IsNullOrWhiteSpace(article.Title) || article.Title.Length > 500 ||
                string.IsNullOrWhiteSpace(article.ContentHtml) || string.IsNullOrWhiteSpace(article.Slug) ||
                article.Slug.Length > 200 || !Regex.IsMatch(article.Slug, @"\A[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*\z") ||
                article.Status != "published" || article.PublishedAt == default || article.UpdatedAt == default || article.CreatedAt == default)
                return Results.BadRequest(new { error = "invalid_article" });

            await store.DeliveryLock.WaitAsync(context.RequestAborted);
            try
            {
                var existing = store.Find(article.Id);
                if (existing is not null && article.UpdatedAt <= existing.Article.UpdatedAt)
                    return Results.Ok(new { url = origin + "/blog/" + Uri.EscapeDataString(existing.Article.Slug) });
                // Keep permanent URLs. Resolve same-slug articles (including translations) without overwriting another ID.
                var slug = existing?.Article.Slug ?? article.Slug.ToLowerInvariant();
                if (existing is null)
                {
                    var requested = slug;
                    var suffix = 0;
                    while (store.Find(slug) is not null)
                        slug = requested + "-" + article.Id + (suffix++ == 0 ? "" : "-" + suffix);
                }
                article = article with { Slug = slug };
                var hero = await images.DownloadAsync(article.HeroImageUrl, store.ImageDirectory, context.RequestAborted);
                var infographic = article.InfographicImageUrl == article.HeroImageUrl ? hero :
                    await images.DownloadAsync(article.InfographicImageUrl, store.ImageDirectory, context.RequestAborted);
                var html = SanitizeHtml(article, hero, infographic);
                store.Upsert(new BlogPost(article, html, hero, infographic));
                return Results.Ok(new { url = origin + "/blog/" + Uri.EscapeDataString(slug) });
            }
            finally { store.DeliveryLock.Release(); }
        }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid_json" }); }
        catch (InvalidDataException) { return Results.Json(new { error = "payload_too_large" }, statusCode: 413); }
        catch (Exception exception)
        {
            // Do not log request bodies, image URLs, authorization headers or secrets.
            logger.LogError("AutoSEO delivery failed ({ErrorType}); the sender should retry.", exception.GetType().Name);
            return Results.Json(new { error = "publication_failed" }, statusCode: 500);
        }
    }

    public static string SanitizeHtml(AutoSeoArticle article, string? hero, string? infographic)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "strong", "em", "b", "i", "u", "s", "blockquote", "pre", "code", "a", "img", "figure", "figcaption", "table", "thead", "tbody", "tr", "th", "td", "caption", "div", "span", "sup", "sub" })
            sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "src", "alt", "title", "colspan", "rowspan" }) sanitizer.AllowedAttributes.Add(attribute);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https"); sanitizer.AllowedSchemes.Add("http"); sanitizer.AllowedSchemes.Add("mailto");
        using var document = new HtmlParser().ParseDocument(sanitizer.Sanitize(article.ContentHtml));
        foreach (var img in document.QuerySelectorAll("img").ToArray())
        {
            var source = img.GetAttribute("src");
            var local = source == article.HeroImageUrl ? hero : source == article.InfographicImageUrl ? infographic : null;
            // Only downloaded media can appear in the article. Other remote images are omitted.
            if (local is null) { img.Remove(); continue; }
            img.SetAttribute("src", local);
            img.SetAttribute("loading", "lazy");
            if (local == hero) img.SetAttribute("alt", article.HeroImageAlt ?? "");
        }
        return document.Body?.InnerHtml ?? "";
    }
}
