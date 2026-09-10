using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Trophy.Catalogue.Services;

// Opt-in post-deployment/CMS publication monitor. Only validated public sitemap pages
// are submitted. A persistent digest avoids resubmission on ordinary process restarts.
public sealed class IndexNowPublisher(IConfiguration config, IWebHostEnvironment environment,
    IHttpClientFactory clients, ILogger<IndexNowPublisher> logger) : BackgroundService
{
    public static string? Key(IConfiguration config)
    {
        var value = config["INDEXNOW_KEY"];
        return value is not null && Regex.IsMatch(value, "\\A[a-zA-Z0-9-]{8,128}\\z") ? value : null;
    }

    public static bool PublicPath(string path) => ProductPages.All.Any(p => p.Path == path) ||
        path is "/" or "/privacy.html" or "/blog" or "/integrations/intelligent-golf/" or
            "/uk/how-to-catalogue-trophy-winners/" or "/us/how-to-catalog-trophy-winners/" ||
        Regex.IsMatch(path, "\\A/blog/[a-z0-9]+(?:-[a-z0-9]+)*\\z");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bool.TryParse(config["INDEXNOW_ENABLED"], out var enabled) || !enabled) return;
        var key = Key(config);
        if (key is null) { logger.LogWarning("IndexNow requires a valid INDEXNOW_KEY."); return; }
        var origin = BlogEndpoints.PublicOrigin(config);
        var client = clients.CreateClient(nameof(IndexNowPublisher));
        var state = Path.Combine(AppDataPath.Resolve(environment, config), "indexnow-digest.txt");
        while (!stoppingToken.IsCancellationRequested)
        {
            // Give the deployment time to become reachable before testing its public URLs.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            try
            {
                var verification = await client.GetStringAsync(origin + "/" + key + ".txt", stoppingToken);
                if (verification.Trim() != key) throw new InvalidOperationException("IndexNow verification is not deployed.");
                var xml = XDocument.Parse(await client.GetStringAsync(origin + "/sitemap.xml", stoppingToken));
                XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
                var urls = xml.Descendants(ns + "loc").Select(e => e.Value).Distinct().Order().ToArray();
                if (urls.Length is 0 or > 10000) throw new InvalidOperationException("Invalid sitemap size.");
                using var evidence = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var url in urls)
                {
                    var uri = new Uri(url);
                    if (uri.GetLeftPart(UriPartial.Authority) != origin || uri.Query.Length > 0 || uri.Fragment.Length > 0 || !PublicPath(uri.AbsolutePath))
                        throw new InvalidOperationException("Sitemap contains a non-public URL.");
                    using var response = await client.GetAsync(url, stoppingToken);
                    response.EnsureSuccessStatusCode();
                    var html = await response.Content.ReadAsStringAsync(stoppingToken);
                    if (response.Headers.TryGetValues("X-Robots-Tag", out var tags) && tags.Any(t => t.Contains("noindex", StringComparison.OrdinalIgnoreCase)) ||
                        Regex.IsMatch(html, "<meta[^>]+name=\"robots\"[^>]+content=\"[^\"]*noindex", RegexOptions.IgnoreCase) ||
                        !html.Contains($"rel=\"canonical\" href=\"{url}\"", StringComparison.Ordinal))
                        throw new InvalidOperationException("Public page canonical/indexing check failed.");
                    evidence.AppendData(Encoding.UTF8.GetBytes(url + Regex.Replace(html, "nonce=\"[^\"]*\"", "nonce=\"\"")));
                }
                var digest = Convert.ToHexString(evidence.GetHashAndReset());
                if (!File.Exists(state) || await File.ReadAllTextAsync(state, stoppingToken) != digest)
                {
                    using var response = await client.PostAsJsonAsync("https://api.indexnow.org/indexnow", new {
                        host = new Uri(origin).Host, key, keyLocation = origin + "/" + key + ".txt", urlList = urls
                    }, stoppingToken);
                    if ((int)response.StatusCode is not (200 or 202)) throw new HttpRequestException("IndexNow did not accept the notification.");
                    await File.WriteAllTextAsync(state, digest, stoppingToken);
                    logger.LogInformation("IndexNow received {Count} public URLs (HTTP {Status}); this does not confirm indexing.", urls.Length, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Do not log request URLs that contain the ownership key.
                logger.LogWarning("IndexNow verification/submission failed ({Error}); retrying on the next interval.", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
