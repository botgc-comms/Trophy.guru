using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class BlogPublishingEndpoints
{
    public const string WebhookPath = "/api/webhooks/writesonic";
    public static void MapBlogPublishing(this WebApplication app) =>
        app.MapPost(WebhookPath, ReceiveAsync).WithMetadata(new RequestBodyLimit(BlogEndpoints.MaxBodyBytes));

    // Negative IDs cannot collide with the positive IDs accepted by the AutoSEO endpoint.
    public static long ArticleId(string documentId)
    {
        var value = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("writesonic:" + documentId))) & long.MaxValue;
        return -(value == 0 ? 1 : value);
    }

    public static async Task<IResult> ReceiveAsync(HttpContext context, BlogStore store, BlogImages images,
        IConfiguration config, ILoggerFactory loggerFactory)
    {
        var token = config["BLOG_PUBLISH_TOKEN"] ?? "";
        if (token.Length < 32) return Results.Json(new { error = "publishing_not_configured" }, statusCode: 503);
        if (!BlogEndpoints.Authorized(context.Request.Headers.Authorization.ToString(), token)) return Results.Unauthorized();
        try
        {
            if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "expected_json" }, statusCode: 415);
            var raw = await BlogImages.ReadBoundedAsync(context.Request.Body, BlogEndpoints.MaxBodyBytes, context.RequestAborted);
            var request = JsonSerializer.Deserialize<BlogPublishRequest>(raw, BlogStore.Json);
            if (request is null) return Results.BadRequest(new { error = "invalid_request" });
            if (request.Mode == "test") return Results.Ok(new { status = "connected", published = false });
            if (request.Mode is not ("validate" or "publish")) return Results.BadRequest(new { error = "invalid_mode" });
            var textOnly = !string.IsNullOrWhiteSpace(request.Text) && string.IsNullOrWhiteSpace(request.ContentHtml);
            var automaticIdentity = textOnly && string.IsNullOrWhiteSpace(request.DocumentId);
            if (textOnly) request = BlogPublishingText.Normalize(request);
            if (string.IsNullOrWhiteSpace(request.DocumentId) || request.DocumentId.Length > 200 ||
                request.DocumentId != request.DocumentId.Trim() || request.DocumentId.Any(char.IsControl) ||
                request.UpdatedAt == default || request.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
                string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500 ||
                string.IsNullOrWhiteSpace(request.ContentHtml) || request.MetaDescription?.Length > 320 ||
                request.HeroImageAlt?.Length > 1000)
                return Results.BadRequest(new { error = "invalid_article", message = "Send text containing the article and its heading, or provide documentId, source updatedAt, title and contentHtml. Description is at most 320 characters." });

            var id = ArticleId(request.DocumentId);
            var slug = request.Slug;
            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = Regex.Replace(request.Title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
                if (slug.Length > 180) slug = slug[..180].TrimEnd('-');
            }
            if (string.IsNullOrEmpty(slug) || slug.Length > 200 || !Regex.IsMatch(slug, @"\A[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*\z"))
                return Results.BadRequest(new { error = "invalid_slug" });
            if (!string.IsNullOrWhiteSpace(request.HeroImageUrl) &&
                (!Uri.TryCreate(request.HeroImageUrl, UriKind.Absolute, out var imageUri) || imageUri.Scheme != "https" ||
                 !imageUri.IsDefaultPort || imageUri.UserInfo.Length > 0 ||
                 (System.Net.IPAddress.TryParse(imageUri.Host, out var ip) && !BlogImages.IsPublicAddress(ip))))
                return Results.BadRequest(new { error = "invalid_image_url" });

            using var document = new HtmlParser().ParseDocument(request.ContentHtml);
            var body = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body!;
            foreach (var element in body.QuerySelectorAll("script,style,nav,header,footer,form,iframe").ToArray()) element.Remove();
            foreach (var h1 in body.QuerySelectorAll("h1").ToArray())
            {
                if (h1.TextContent.Trim().Equals(request.Title.Trim(), StringComparison.OrdinalIgnoreCase)) { h1.Remove(); continue; }
                var h2 = document.CreateElement("h2"); h2.InnerHtml = h1.InnerHtml; h1.Replace(h2);
            }
            var article = new AutoSeoArticle
            {
                Event = "article.published", Id = id, PublishingDocumentId = request.DocumentId,
                Title = request.Title.Trim(), Slug = slug.ToLowerInvariant(), ContentHtml = body.InnerHtml,
                MetaDescription = request.MetaDescription?.Trim() ?? "",
                HeroImageUrl = string.IsNullOrWhiteSpace(request.HeroImageUrl) ? null : request.HeroImageUrl,
                HeroImageAlt = request.HeroImageAlt, Status = "published", LanguageCode = "en-GB",
                CreatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow, UpdatedAt = request.UpdatedAt
            };
            var cleaned = BlogEndpoints.SanitizeHtml(article, null, null);
            using var cleanDocument = new HtmlParser().ParseDocument(cleaned);
            var plain = Regex.Replace(cleanDocument.Body!.TextContent, @"\s+", " ").Trim();
            if (textOnly && plain.Length < 80)
                return Results.BadRequest(new { error = "article_text_too_short", message = "Send a real article from Writesonic, including its title and body. Use mode=test for a connection check without publishing sample text." });
            if (plain.Length == 0) return Results.BadRequest(new { error = "empty_article_after_cleaning" });
            if (article.MetaDescription.Length == 0) article = article with { MetaDescription = plain.Length > 160 ? plain[..157] + "..." : plain };
            var warnings = new List<string>();
            if (body.QuerySelectorAll("img").Any(img => img.GetAttribute("src") != article.HeroImageUrl))
                warnings.Add("Inline images other than the supplied hero image are omitted. Supply heroImageUrl and heroImageAlt to include the cover.");
            // Fingerprint source-controlled fields only; receipt dates must not break retry detection.
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                article.Title, article.Slug, article.ContentHtml, article.MetaDescription, article.HeroImageUrl, article.HeroImageAlt
            }, BlogStore.Json))));
            article = article with { PublishingFingerprint = fingerprint };
            var origin = BlogEndpoints.PublicOrigin(config);
            await store.DeliveryLock.WaitAsync(context.RequestAborted);
            try
            {
                var existing = store.Find(id);
                if (existing is not null && existing.Article.PublishingDocumentId != request.DocumentId)
                    return Results.Conflict(new { error = "document_id_collision" });
                if (existing is not null)
                {
                    var url = origin + "/blog/" + Uri.EscapeDataString(existing.Article.Slug);
                    if (automaticIdentity && fingerprint == existing.Article.PublishingFingerprint)
                        return Results.Ok(new { status = "unchanged", published = request.Mode == "publish", url });
                    if (request.UpdatedAt < existing.Article.UpdatedAt)
                        return Results.Ok(new { status = "ignored_older_revision", published = request.Mode == "publish", url });
                    if (request.UpdatedAt == existing.Article.UpdatedAt)
                    {
                        if (fingerprint != existing.Article.PublishingFingerprint)
                            return Results.Conflict(new { error = "revision_conflict", message = "Changed content requires a later source updatedAt." });
                        return Results.Ok(new { status = "unchanged", published = request.Mode == "publish", url });
                    }
                    article = article with { Slug = existing.Article.Slug, CreatedAt = existing.Article.CreatedAt, PublishedAt = existing.Article.PublishedAt };
                }
                else if (store.Find(article.Slug) is not null)
                    return Results.Conflict(new { error = "slug_already_exists", message = "Choose another slug; existing articles are never replaced by a different document." });

                var destination = origin + "/blog/" + Uri.EscapeDataString(article.Slug);
                if (request.Mode == "validate") return Results.Ok(new
                {
                    status = "validated", published = false, url = destination, warnings,
                    note = "No article or image saved. Image download is checked during publication."
                });
                var hero = await images.DownloadAsync(article.HeroImageUrl, store.ImageDirectory, context.RequestAborted);
                var html = BlogEndpoints.SanitizeHtml(article, hero, null);
                store.Upsert(new BlogPost(article, html, hero, null));
                return Results.Ok(new { status = existing is null ? "published" : "updated", published = true, url = destination, warnings });
            }
            finally { store.DeliveryLock.Release(); }
        }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid_json" }); }
        catch (InvalidDataException) { return Results.Json(new { error = "payload_too_large" }, statusCode: 413); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("BlogPublishing").LogError("Blog publication failed ({ErrorType}); delivery can be retried.", exception.GetType().Name);
            return Results.Json(new { error = "publication_failed" }, statusCode: 500);
        }
    }
}
