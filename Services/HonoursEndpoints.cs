using System.Security.Claims;
using System.Text.RegularExpressions;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class HonoursEndpoints
{
    public static void Map(WebApplication app, string webRootPath)
    {
        app.MapGet("/api/publication", async (HttpContext context, HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            var account = CurrentAccount(context);
            if (account?.ClubId is null) return Results.Unauthorized();
            var publication = await store.GetAsync(account.ClubId, cancellationToken);
            return Results.Ok(new { publication = Status(publication), candidates = await store.GetCandidatesAsync(account.ClubId, cancellationToken),
                canPublish = AccountSecurity.IsOwner(account) && AccountSecurity.IsEmailVerified(account), canWithdraw = AccountSecurity.IsOwner(account),
                publicUrl = $"/honours/{Uri.EscapeDataString(account.ClubId)}", embedUrl = $"/embed/{Uri.EscapeDataString(account.ClubId)}" });
        });

        app.MapPost("/api/publication/preview", async (HttpContext context, HonoursPublicationOptions options,
            HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            var account = CurrentAccount(context);
            if (account?.ClubId is null) return Results.Unauthorized();
            try { return Results.Ok(await store.PreviewAsync(account.ClubId, options, cancellationToken)); }
            catch (PublicationException exception) { return Invalid(exception); }
        }).WithMetadata(new RequestBodyLimit(1024 * 1024));

        app.MapPost("/api/publication/publish", async (HttpContext context, PublishHonoursInput input,
            HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            var account = CurrentAccount(context);
            if (account?.ClubId is null) return Results.Unauthorized();
            if (!AccountSecurity.IsOwner(account) || !AccountSecurity.IsEmailVerified(account)) return Results.Json(new {
                error = "publication_owner_required", message = "A club owner with a verified email address must approve publication." }, statusCode: 403);
            try { return Results.Ok(new { publication = Status(await store.PublishAsync(account.ClubId, account.Id, input, cancellationToken)) }); }
            catch (PublicationException exception) { return Invalid(exception); }
        }).WithMetadata(new RequestBodyLimit(1024 * 1024));

        app.MapPost("/api/publication/withdraw", async (HttpContext context, HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            var account = CurrentAccount(context);
            if (account?.ClubId is null) return Results.Unauthorized();
            if (!AccountSecurity.IsOwner(account)) return Results.Json(new { error = "owner_required", message = "Only a club owner can withdraw publication." }, statusCode: 403);
            return Results.Ok(new { publication = Status(await store.WithdrawAsync(account.ClubId, account.Id, cancellationToken)) });
        });

        app.MapGet("/api/publication/preview-assets/logo", async (HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            var account = CurrentAccount(context);
            if (account?.ClubId is null) return Results.Unauthorized();
            var asset = await accounts.GetLogoForClubAsync(account.ClubId, cancellationToken);
            return asset is null ? Results.NotFound() : Results.File(asset.Value.Path, asset.Value.ContentType);
        });

        app.MapGet("/api/publication/preview-assets/trophies/{trophyId}", async (string trophyId, HttpContext context,
            CatalogueStore catalogue, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            if (CurrentAccount(context)?.ClubId is null) return Results.Unauthorized();
            var trophy = await catalogue.GetTrophyAsync(trophyId, cancellationToken);
            if (trophy is null) return Results.NotFound();
            var asset = await catalogue.GetIllustrationPathAsync(trophyId, cancellationToken);
            if (asset is null && trophy.ReferenceImage is { } reference && reference.StartsWith("/catalogue/", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(webRootPath, "catalogue", Path.GetFileNameWithoutExtension(reference) + ".png");
                if (File.Exists(candidate)) asset = candidate;
            }
            return asset is null ? Results.NotFound() : Results.File(asset, "image/png");
        });

        // This route contains an empty display shell. Only the authenticated same-origin parent supplies its private preview.
        app.MapGet("/honours-preview", async (HttpContext context) =>
        {
            PrivateResponse(context);
            SetBoardSecurity(context, "'self'");
            return Results.Content(await BoardHtmlAsync(webRootPath, true, context.RequestAborted), "text/html; charset=utf-8");
        });

        app.MapGet("/honours/{clubId}", async (string clubId, HttpContext context, HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            if (!HonoursPublicationStore.ValidClubId(clubId)) return Results.NotFound();
            var publication = await store.GetAsync(clubId, cancellationToken);
            if (!publication.IsPublic || publication.Snapshot is null) return Results.NotFound();
            SetBoardSecurity(context, "'none'");
            return Results.Content(await BoardHtmlAsync(webRootPath, true, cancellationToken), "text/html; charset=utf-8");
        });

        app.MapGet("/embed/{clubId}", async (string clubId, HttpContext context, HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            if (!HonoursPublicationStore.ValidClubId(clubId)) return Results.NotFound();
            var publication = await store.GetAsync(clubId, cancellationToken);
            if (!publication.IsPublic || publication.Snapshot is null) return Results.NotFound();
            var origins = HonoursPublicationStore.NormalizeOptions(publication.Options).AllowedEmbedOrigins;
            SetBoardSecurity(context, "'self'" + (origins.Count > 0 ? " " + string.Join(' ', origins) : ""));
            return Results.Content(await BoardHtmlAsync(webRootPath, true, cancellationToken), "text/html; charset=utf-8");
        });

        app.MapGet("/api/public/clubs/{clubId}/honours", async (string clubId, HttpContext context,
            HonoursPublicationStore store, CancellationToken cancellationToken) =>
        {
            PrivateResponse(context);
            if (!HonoursPublicationStore.ValidClubId(clubId)) return Results.NotFound();
            var publication = await store.GetAsync(clubId, cancellationToken);
            return publication.IsPublic && publication.Snapshot is not null ? Results.Ok(publication.Snapshot) : Results.NotFound();
        });

        app.MapGet("/api/public/clubs/{clubId}/logo", (string clubId, HttpContext context,
            HonoursPublicationStore store, CancellationToken cancellationToken) => PublicAssetAsync(clubId, "logo", context, store, cancellationToken));
        app.MapGet("/api/public/clubs/{clubId}/trophies/{trophyId}/illustration", (string clubId, string trophyId,
            HttpContext context, HonoursPublicationStore store, CancellationToken cancellationToken) =>
            PublicAssetAsync(clubId, $"trophy:{trophyId}", context, store, cancellationToken));
    }

    private static async Task<IResult> PublicAssetAsync(string clubId, string key, HttpContext context,
        HonoursPublicationStore store, CancellationToken cancellationToken)
    {
        PrivateResponse(context);
        if (!HonoursPublicationStore.ValidClubId(clubId)) return Results.NotFound();
        var asset = await store.GetPublicAssetAsync(clubId, key, cancellationToken);
        return asset is null ? Results.NotFound() : Results.File(asset.Value.Path, asset.Value.ContentType);
    }

    private static object Status(HonoursPublication publication) => new
    {
        publication.IsPublic, publication.Revision, publication.PublishedAt, publication.WithdrawnAt,
        publication.Options, summary = publication.Snapshot?.Summary, publication.Audit
    };
    private static AccountRecord? CurrentAccount(HttpContext context) => context.Items["account"] as AccountRecord;
    private static IResult Invalid(PublicationException exception) => Results.Json(new { error = exception.Code, message = exception.Message },
        statusCode: exception.Code is "preview_changed" or "selection_changed" ? 409 : 400);
    private static void PrivateResponse(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-Robots-Tag"] = "noindex,nofollow,noarchive";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    private static void SetBoardSecurity(HttpContext context, string ancestors)
    {
        context.Response.Headers.Remove("X-Frame-Options");
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; script-src 'self'; connect-src 'self'; frame-ancestors " + ancestors + "; base-uri 'self'; form-action 'self'";
    }
    private static async Task<string> BoardHtmlAsync(string webRootPath, bool isolated, CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(webRootPath, "honours.html"), cancellationToken);
        if (!isolated) return html;
        // Embeds and private previews never initialise analytics, consent storage or third-party font requests.
        html = Regex.Replace(html, @"\s*<script[^>]+src=""/analytics\.js[^""]*""[^>]*></script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\s*<link[^>]+(?:analytics\.css|fonts\.googleapis\.com|fonts\.gstatic\.com)[^>]*>", "", RegexOptions.IgnoreCase);
        return html;
    }
}
