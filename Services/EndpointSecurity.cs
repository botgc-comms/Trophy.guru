using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace Trophy.Catalogue.Services;

public sealed record VerifiedArchiveOperation;
public sealed record ResourceArchiveOperation;
public sealed record RequestBodyLimit(long Bytes);

public static class EndpointSecurity
{
    public static RouteHandlerBuilder VerifiedOperation(this RouteHandlerBuilder endpoint, long bodyLimit = 128 * 1024) =>
        endpoint.WithMetadata(new VerifiedArchiveOperation(), new RequestBodyLimit(bodyLimit));

    public static RouteHandlerBuilder ResourceOperation(this RouteHandlerBuilder endpoint) =>
        endpoint.WithMetadata(new ResourceArchiveOperation());

    public static bool IsVerifiedOperation(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<VerifiedArchiveOperation>() is not null;

    public static bool IsResourceOperation(HttpContext context) => IsVerifiedOperation(context) || context.GetEndpoint()?.Metadata.GetMetadata<ResourceArchiveOperation>() is not null || context.GetEndpoint()?.Metadata.GetMetadata<RequestBodyLimit>()?.Bytes > 128 * 1024;

    public static void ConfigureLimits(RateLimiterOptions options)
    {
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("server", _ => new ConcurrencyLimiterOptions { PermitLimit = 32, QueueLimit = 0 })),
            PartitionedRateLimiter.Create<HttpContext, string>(context => IsResourceOperation(context)
                ? RateLimitPartition.GetConcurrencyLimiter("resource-work", _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter("other")),
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
                var path = context.Request.Path;
                // Stripe retries use its signature/idempotency controls, not a browser-account limiter.
                if (path.Equals("/api/billing/webhook", StringComparison.OrdinalIgnoreCase)) return RateLimitPartition.GetNoLimiter("webhook");
                var publicRead = path.StartsWithSegments("/api/public") || path.StartsWithSegments("/honours") || path.StartsWithSegments("/embed");
                if (!mutation && !publicRead && !IsResourceOperation(context)) return RateLimitPartition.GetNoLimiter("ordinary-read");
                var identity = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
                var kind = IsResourceOperation(context) ? "resource" : mutation ? "write" : "public";
                var permits = kind == "resource" ? 20 : kind == "write" ? 90 : 600;
                return RateLimitPartition.GetFixedWindowLimiter(kind + ":" + identity, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
            }));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsJsonAsync(new { error = "request_limit", message = "Too many requests are being processed. Please wait a minute and try again." }, cancellationToken);
        };
    }

    public static async Task<bool> ApplyBodyLimitAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)) return true;
        var limit = context.GetEndpoint()?.Metadata.GetMetadata<RequestBodyLimit>()?.Bytes ??
            (context.Request.Path.StartsWithSegments("/api/auth") ? 16 * 1024 : context.Request.Path.Equals("/api/billing/webhook", StringComparison.OrdinalIgnoreCase) ? 1024 * 1024 : 128 * 1024);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = limit;
        if (context.Request.ContentLength is not > 0 || context.Request.ContentLength <= limit) return true;
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new { error = "request_too_large", message = "This request is too large. Reduce the file or number of records and try again." });
        return false;
    }
}
