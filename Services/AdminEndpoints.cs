using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class AdminEndpoints
{
    public const string Scheme = "TrophyGuruSiteAdmin";
    public static bool IsAllowed(AccountRecord? account, IConfiguration configuration) =>
        account is not null && account.EmailVerifiedAt.HasValue &&
        !string.IsNullOrWhiteSpace(configuration["SITE_ADMIN_EMAIL"]) &&
        string.Equals(account.NormalizedEmail, configuration["SITE_ADMIN_EMAIL"]!.Trim(), StringComparison.OrdinalIgnoreCase);

    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = builder.Environment.IsDevelopment() ? "trophy_guru_admin" : "__Host-trophy_guru_admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var account = id is null ? null : await accounts.GetAccountAsync(id, context.HttpContext.RequestAborted);
                if (!IsAllowed(account, context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()) ||
                    !AccountSecurity.IsSessionCurrent(context.Principal!, account!))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(Scheme);
                }
            };
        });
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/admin", () => Results.Content(Page, "text/html"));
        app.MapPost("/admin/login", async (LoginInput input, HttpContext context, AccountStore accounts, IConfiguration config, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(config["SITE_ADMIN_EMAIL"]))
                return Results.Json(new { message = "Owner access has not been configured. Add SITE_ADMIN_EMAIL in Render with your Trophy Guru account email, then redeploy." }, statusCode: 503);
            var account = await accounts.AuthenticateAsync(input, ct);
            if (account is null)
                return Results.Json(new { message = "That email and password combination was not recognised. Use your registered Trophy Guru account, or reset its password using the link below." }, statusCode: 401);
            // Only give account-specific guidance after the password has been verified.
            if (!string.Equals(account.NormalizedEmail, config["SITE_ADMIN_EMAIL"]!.Trim(), StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { message = "Your password is correct, but this account is not the configured site owner. Check that SITE_ADMIN_EMAIL in Render matches this account email, then redeploy." }, statusCode: 403);
            if (!account.EmailVerifiedAt.HasValue)
                return Results.Json(new { message = "Your password is correct, but this account's email has not been verified. Open Account security below after signing into your archive. Original archive accounts need a regular registered, email-verified account for administration." }, statusCode: 403);
            await context.SignInAsync(Scheme, AccountSecurity.CreatePrincipal(account!), new AuthenticationProperties
            { IsPersistent = false, AllowRefresh = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
            app.Logger.LogInformation("Site admin signed in: {AccountId}", account!.Id);
            return Results.Ok();
        }).WithMetadata(new RequestBodyLimit(16 * 1024)).RequireRateLimiting("authentication");
        app.MapPost("/admin/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Ok();
        });
        var data = app.MapGroup("/admin/data");
        data.AddEndpointFilter(async (context, next) =>
        {
            var auth = await context.HttpContext.AuthenticateAsync(Scheme);
            if (!auth.Succeeded) return Results.Unauthorized();
            context.HttpContext.Items["site-admin-id"] = auth.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
            return await next(context);
        });
        data.MapGet("/registrations", async (AccountStore accounts, CancellationToken ct) =>
            Results.Ok(await accounts.GetAdminRegistrationsAsync(ct)));
        data.MapGet("/clubs/{clubId}", async Task<IResult> (string clubId, HttpContext context, AccountStore accounts, CatalogueStore store, ClubContextAccessor clubs, CancellationToken ct) =>
        {
            var club = await accounts.GetClubAsync(clubId, ct);
            if (club is null) return Results.NotFound();
            using var scope = clubs.Push(club.Id);
            var trophies = await store.GetTrophiesAsync(ct);
            app.Logger.LogInformation("Site admin {AccountId} viewed club uploads: {ClubId}", context.Items["site-admin-id"], club.Id);
            return Results.Ok(new { club.Name, trophies = trophies.Select(t => new {
                t.Id, t.Name, t.Status, t.Archived,
                images = t.Evidence.Select(i => ImageInfo(club.Id, t.Id, "evidence", i))
                    .Concat(t.TrophyPhotos.Select(i => ImageInfo(club.Id, t.Id, "photo", i))).OrderByDescending(i => i.UploadedAt)
            }) });
        });
        data.MapGet("/clubs/{clubId}/trophies/{trophyId}/{kind}/{imageId}", async Task<IResult> (
            string clubId, string trophyId, string kind, string imageId, AccountStore accounts, CatalogueStore store, ClubContextAccessor clubs, CancellationToken ct) =>
        {
            if (kind is not ("evidence" or "photo")) return Results.NotFound();
            var club = await accounts.GetClubAsync(clubId, ct);
            if (club is null) return Results.NotFound();
            using var scope = clubs.Push(club.Id);
            var trophy = await store.GetTrophyAsync(trophyId, ct);
            var image = (kind == "evidence" ? trophy?.Evidence : trophy?.TrophyPhotos)?.FirstOrDefault(i => i.Id == imageId);
            if (image is null || image.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return Results.NotFound();
            var path = kind == "evidence" ? await store.GetEvidencePathAsync(trophyId, imageId, ct) : await store.GetTrophyPhotoPathAsync(trophyId, imageId, ct);
            return path is null ? Results.NotFound() : Results.File(path, image.ContentType);
        });
    }

    private sealed record AdminImage(string Name, string Kind, DateTimeOffset UploadedAt, string Url);
    private static AdminImage ImageInfo(string club, string trophy, string kind, EvidenceImage image) =>
        new(image.OriginalName, kind == "evidence" ? image.Kind : "Trophy photo", image.UploadedAt,
            $"/admin/data/clubs/{Uri.EscapeDataString(club)}/trophies/{Uri.EscapeDataString(trophy)}/{kind}/{Uri.EscapeDataString(image.Id)}");

    private const string Page = """
<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Owner dashboard | Trophy Guru</title><meta name="robots" content="noindex,nofollow,noarchive">
<link rel="icon" href="/favicon.svg"><link rel="stylesheet" href="/admin.css"><script src="/admin.js" defer></script></head>
<body><header><a href="/" aria-label="Trophy Guru home"><img src="/images/brand/trophy-guru-logo-transparent.png" width="200" height="54" alt="Trophy Guru"></a><span>Owner dashboard</span><button id="logout" hidden>Sign out</button></header>
<main><section id="login-panel"><p class="eyebrow">Private administration</p><h1>Sign in to your dashboard</h1><p>Use your verified Trophy Guru owner account. Admin sessions last 30 minutes.</p>
<form id="login"><label>Email<input name="email" type="email" autocomplete="username" required maxlength="254"></label><label>Password<input name="password" type="password" autocomplete="current-password" required maxlength="128"></label><button>Sign in securely</button></form><p><a href="/account-security.html#forgot">Reset password</a> · <a href="/account-security.html#settings">Account security</a> · <a href="/archive.html#signup">Create an account</a></p></section>
<p id="message" role="status" aria-live="polite"></p>
<section id="dashboard" hidden><p class="eyebrow">Your service at a glance</p><h1>Registrations</h1><p id="totals"></p><label>Find an account or club<input id="search" type="search" placeholder="Name, email or club"></label><button id="refresh">Refresh registrations</button>
<div class="table-wrap"><table><thead><tr><th>Name and email</th><th>Registered</th><th>Email verified</th><th>Club</th><th>Uploads</th></tr></thead><tbody id="registrations"></tbody></table></div>
<section id="gallery" hidden aria-labelledby="gallery-title"><h2 id="gallery-title" tabindex="-1"></h2><p id="gallery-summary"></p><div id="trophies"></div></section></section></main></body></html>
""";
}
