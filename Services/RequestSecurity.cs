namespace Trophy.Catalogue.Services;

public static class RequestSecurity
{
    public static bool IsSameOriginMutation(HttpRequest request, IConfiguration configuration)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return true;
        // Stripe authenticates the raw request body using its webhook signature.
        if (request.Path == "/api/billing/webhook") return true;
        if (request.Headers["Sec-Fetch-Site"] == "cross-site") return false;
        var source = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(source)) source = request.Headers.Referer.ToString();
        if (!Uri.TryCreate(source, UriKind.Absolute, out var origin) || origin.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(origin.UserInfo)) return false;
        var expected = configuration["PUBLIC_SITE_URL"] ?? configuration["RENDER_EXTERNAL_URL"] ?? $"{request.Scheme}://{request.Host}";
        return Uri.TryCreate(expected, UriKind.Absolute, out var site) && string.Equals(origin.GetLeftPart(UriPartial.Authority), site.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}
