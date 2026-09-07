using System.Net;

namespace Trophy.Catalogue.Services;

public static class SearchDiscovery
{
    // Verification values come from the owner's Search Console / Webmaster Tools account.
    // Encoding is necessary because these settings are inserted into an HTML attribute.
    public static string VerificationTags(IConfiguration configuration)
    {
        var tags = new System.Text.StringBuilder();
        foreach (var (setting, name) in new[]
        {
            ("GOOGLE_SITE_VERIFICATION", "google-site-verification"),
            ("BING_SITE_VERIFICATION", "msvalidate.01")
        })
        {
            var value = configuration[setting]?.Trim();
            if (!string.IsNullOrEmpty(value))
                tags.Append($"<meta name=\"{name}\" content=\"{WebUtility.HtmlEncode(value)}\">\n");
        }
        return tags.ToString();
    }
}
