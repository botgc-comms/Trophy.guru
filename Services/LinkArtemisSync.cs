using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Markdig;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

// Latitude's documented API exposes completed articles, not delivery webhooks or revision dates.
// Import each source ID once. Never use receipt time to overwrite a later editorial correction.
public sealed class LinkArtemisSync(IConfiguration config, IHttpClientFactory clients, BlogStore store,
    ILogger<LinkArtemisSync> logger, BlogImages images) : BackgroundService
{
    public const string ClientName = "LinkArtemis";
    public const string ApiRoot = "https://app.linkartemis.com/api/v1/articles";
    public sealed record Article(
        string Id, string? Title, string? Slug,
        [property: JsonPropertyName("meta_description")] string? MetaDescription,
        string? Excerpt,
        [property: JsonPropertyName("content_html")] string? ContentHtml,
        [property: JsonPropertyName("content_markdown")] string? ContentMarkdown,
        [property: JsonPropertyName("language_code")] string? LanguageCode,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("hero_image_url")] string? HeroImageUrl = null);
    public sealed record SyncStatus(bool Configured, bool Enabled, string State,
        DateTimeOffset? LastCheckedAt = null, int Imported = 0, int Skipped = 0, int ImagesUpdated = 0);
    public sealed class ApiException(int status, TimeSpan retryAfter) : Exception("Latitude API HTTP " + status)
    {
        public int Status { get; } = status;
        public TimeSpan RetryAfter { get; } = retryAfter;
    }
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private SyncStatus status = new(false, false, "starting");
    public bool Configured => !string.IsNullOrWhiteSpace(config["LINKARTEMIS_API_KEY"]);
    public bool Enabled => Configured && bool.TryParse(config["LINKARTEMIS_ENABLED"], out var enabled) && enabled;
    public SyncStatus Status => Volatile.Read(ref status) with { Configured = Configured, Enabled = Enabled };
    public static long ArticleId(string id)
    {
        var hash = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("linkartemis:" + Guid.Parse(id).ToString("D")))) & long.MaxValue;
        return -(hash == 0 ? 1 : hash);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled) { Volatile.Write(ref status, new(Configured, Enabled, "disabled")); return; }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMinutes(15);
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    var state = error is ApiException api ? "api_" + api.Status : "sync_failed";
                    Volatile.Write(ref status, Status with { State = state, LastCheckedAt = DateTimeOffset.UtcNow });
                    // Do not log the response body, API key, full exception or generated article text.
                    logger.LogWarning("Latitude article sync failed: {State} ({ErrorType}). Will retry.", state, error.GetType().Name);
                    if (error is ApiException limited && limited.RetryAfter > delay) delay = limited.RetryAfter;
                }
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!Enabled) { Volatile.Write(ref status, new(Configured, Enabled, "disabled")); return; }
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            var imported = 0; var skipped = 0; var fetched = 0; var imagesUpdated = 0;
            using var client = clients.CreateClient(ClientName);
            // Optional allowlist for workspaces later shared with another website; empty means all.
            var selected = (config["LINKARTEMIS_ARTICLE_IDS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<Guid>();
            for (var page = 0; page < 20; page++)
            {
                var summaries = await ReadAsync<Article[]>(client, ApiRoot + "?limit=100&offset=" + page * 100, cancellationToken);
                foreach (var summary in summaries)
                {
                    if (!Guid.TryParse(summary.Id, out var sourceId)) { skipped++; continue; }
                    if (!seen.Add(sourceId)) continue;
                    var id = sourceId.ToString("D");
                    if (selected.Length > 0 && !selected.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                    var existing = store.Find(ArticleId(id));
                    if (existing is not null)
                    {
                        if (existing.Article.PublishingDocumentId != "linkartemis:" + id) skipped++;
                        else if (existing.HeroPath is null && !string.IsNullOrWhiteSpace(summary.HeroImageUrl))
                        {
                            if (await RestoreCoverAsync(summary, cancellationToken)) imagesUpdated++; else skipped++;
                        }
                        continue;
                    }
                    Article detail;
                    try { detail = await ReadAsync<Article>(client, ApiRoot + "/" + id, cancellationToken); }
                    catch (ApiException error) when (error.Status == 404) { skipped++; continue; }
                    fetched++;
                    if (!Guid.TryParse(detail.Id, out var detailId) || detailId != sourceId) { skipped++; continue; }
                    if (await ImportAsync(detail, cancellationToken)) imported++; else skipped++;
                    if (fetched >= 25) { Complete("batch_limit", imported, skipped, imagesUpdated); return; }
                }
                if (summaries.Length < 100) { Complete(skipped > 0 ? "completed_with_skips" : "ok", imported, skipped, imagesUpdated); return; }
            }
            Complete("scan_limit", imported, skipped, imagesUpdated);
        }
        finally { syncLock.Release(); }
    }
    private void Complete(string state, int imported, int skipped, int imagesUpdated)
    {
        Volatile.Write(ref status, new(Configured, Enabled, state, DateTimeOffset.UtcNow, imported, skipped, imagesUpdated));
        logger.LogInformation("Latitude sync {State}: {Imported} articles published, {Skipped} rejected, {ImagesUpdated} covers restored. Existing article text preserved.", state, imported, skipped, imagesUpdated);
    }
    private async Task<T> ReadAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
    {
        // 60/minute provider limit; space every request, including summary and missing-detail requests.
        await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-Key", config["LINKARTEMIS_API_KEY"]);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retry = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date is { } until ? until - DateTimeOffset.UtcNow : TimeSpan.Zero);
            throw new ApiException((int)response.StatusCode, retry);
        }
        using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = await BlogImages.ReadBoundedAsync(body, BlogEndpoints.MaxBodyBytes, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, BlogStore.Json) ?? throw new JsonException("Empty Latitude response.");
    }
    public async Task<bool> ImportAsync(Article source, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(source.Id, out var guid) || string.IsNullOrWhiteSpace(source.Title) || source.Title.Length > 500 ||
            source.CreatedAt == default || source.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5)) return false;
        var language = (source.LanguageCode ?? "en").ToLowerInvariant();
        if (language is not ("us" or "uk" or "gb" or "en") && !language.StartsWith("en-")) return false;
        var html = source.ContentHtml;
        if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(source.ContentMarkdown)) html = Markdown.ToHtml(source.ContentMarkdown);
        if (string.IsNullOrWhiteSpace(html) || Encoding.UTF8.GetByteCount(html) > BlogEndpoints.MaxBodyBytes) return false;
        using var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("script,style,nav,header,footer,form,iframe").ToArray()) element.Remove();
        var body = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body!;
        foreach (var h1 in body.QuerySelectorAll("h1").ToArray())
        {
            if (h1.TextContent.Trim().Equals(source.Title.Trim(), StringComparison.OrdinalIgnoreCase)) h1.Remove();
            else { var h2 = document.CreateElement("h2"); h2.InnerHtml = h1.InnerHtml; h1.Replace(h2); }
        }
        var slug = (source.Slug ?? "").Trim().ToLowerInvariant();
        if (slug.Length == 0) slug = Regex.Replace(source.Title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
        if (slug.Length is 0 or > 200 || !Regex.IsMatch(slug, @"\A[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*\z")) return false;
        var now = DateTimeOffset.UtcNow;
        var article = new AutoSeoArticle {
            Id = ArticleId(source.Id), PublishingDocumentId = "linkartemis:" + guid.ToString("D"),
            Event = "article.published", Title = source.Title.Trim(), Slug = slug, ContentHtml = body.InnerHtml,
            CreatedAt = source.CreatedAt, PublishedAt = now, UpdatedAt = now,
            Status = "published", LanguageCode = language == "us" ? "en-US" : language is "gb" or "uk" ? "en-GB" : language
        };
        var clean = BlogEndpoints.SanitizeHtml(article, null, null);
        using var cleaned = new HtmlParser().ParseDocument(clean);
        var plain = Regex.Replace(cleaned.Body!.TextContent, @"\s+", " ").Trim();
        if (plain.Length < 80) return false;
        var description = string.IsNullOrWhiteSpace(source.MetaDescription) ? source.Excerpt : source.MetaDescription;
        if (string.IsNullOrWhiteSpace(description)) description = plain.Length > 160 ? plain[..157] + "..." : plain;
        if (description.Length > 320) description = description[..317] + "...";
        article = article with { ContentHtml = clean, MetaDescription = description.Trim() };
        await store.DeliveryLock.WaitAsync(cancellationToken);
        try
        {
            // Recheck under the same lock as incoming webhooks. Slug collisions never overwrite a different post.
            if (store.Find(article.Id) is not null || store.Find(slug) is not null) return false;
            var cover = await DownloadCoverAsync(source.HeroImageUrl, cancellationToken);
            article = WithCover(article, source.HeroImageUrl, cover);
            store.Upsert(new(article, clean, cover, null));
            return true;
        }
        finally { store.DeliveryLock.Release(); }
    }
    // Repair only the missing cover; use the latest stored body and metadata, not a provider revision.
    public async Task<bool> RestoreCoverAsync(Article source, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(source.Id, out var id) || string.IsNullOrWhiteSpace(source.HeroImageUrl)) return false;
        await store.DeliveryLock.WaitAsync(cancellationToken);
        try
        {
            var post = store.Find(ArticleId(source.Id));
            if (post is null || post.HeroPath is not null || post.Article.PublishingDocumentId != "linkartemis:" + id.ToString("D")) return false;
            var cover = await DownloadCoverAsync(source.HeroImageUrl, cancellationToken);
            if (cover is null) return false;
            var now = DateTimeOffset.UtcNow;
            var article = WithCover(post.Article, source.HeroImageUrl, cover) with {
                UpdatedAt = now > post.Article.UpdatedAt ? now : post.Article.UpdatedAt.AddTicks(1)
            };
            store.Upsert(post with { Article = article, HeroPath = cover });
            return true;
        }
        finally { store.DeliveryLock.Release(); }
    }
    private async Task<string?> DownloadCoverAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try { return await images.DownloadAsync(url, store.ImageDirectory, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or UriFormatException or OperationCanceledException or InvalidDataException)
        {
            logger.LogWarning("Latitude cover download failed ({ErrorType}); the article stays readable and the cover will be retried.", error.GetType().Name);
            return null;
        }
    }
    private AutoSeoArticle WithCover(AutoSeoArticle article, string? sourceUrl, string? cover)
    {
        if (cover is null) return article;
        var size = BlogImages.Dimensions(File.ReadAllBytes(Path.Combine(store.ImageDirectory, Path.GetFileName(cover))));
        // Latitude provides no image description. An empty alt avoids inventing details about stock art.
        return article with { HeroImageUrl = sourceUrl, HeroImageAlt = "", HeroImageWidth = size?.Width, HeroImageHeight = size?.Height };
    }

}
