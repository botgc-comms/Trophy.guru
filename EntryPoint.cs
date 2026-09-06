using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;

namespace Trophy.Catalogue;

public static class EntryPoint
{
    private const string AuthenticationScheme = "TrophyArchiveAccount";
    private static readonly HashSet<string> AcceptedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public static async Task Main(string[] args)
    {
        if (DataMaintenance.TryRun(args)) return;
        var builder = WebApplication.CreateBuilder(args);
        var dataRoot = AppDataPath.Resolve(builder.Environment, builder.Configuration);
        using var dataLease = new DataDirectoryLease(dataRoot);
        var keyRing = Path.Combine(dataRoot, "key-ring");
        Directory.CreateDirectory(keyRing);

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60 * 1024 * 1024);
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 60 * 1024 * 1024);
        builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing))
            .SetApplicationName("TrophyArchive");
        builder.Services.AddAuthentication(AuthenticationScheme)
            .AddCookie(AuthenticationScheme, options =>
            {
                options.Cookie.Name = builder.Environment.IsDevelopment() ? "trophy_archive_session" : "__Host-trophy_archive_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = AccountSecurity.ValidatePrincipalAsync;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return context.Response.WriteAsJsonAsync(new { error = "forbidden" });
                };
            });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        builder.Services.AddSingleton<IPasswordHasher<AccountRecord>, PasswordHasher<AccountRecord>>();
        builder.Services.AddSingleton<ClubContextAccessor>();
        builder.Services.AddSingleton<AccountStore>();
        builder.Services.AddSingleton<TransactionalEmail>();
        builder.Services.AddSingleton<HonoursPublicationStore>();
        builder.Services.AddSingleton<BillingStore>();
        builder.Services.AddSingleton<StripeBillingService>();
        builder.Services.AddHttpClient(nameof(StripeBillingService));
        builder.Services.AddSingleton<CatalogueStore>();
        builder.Services.AddSingleton<MemberDirectoryStore>();
        builder.Services.AddSingleton<FuzzyMemberMatcher>();
        builder.Services.AddSingleton<MemberMatchingCoordinator>();
        builder.Services.AddSingleton<OpenAiEngravingReader>();
        builder.Services.AddSingleton<OpenAiTrophyIllustrator>();
        builder.Services.AddSingleton<BackgroundAnalysisQueue>();
        builder.Services.AddSingleton<BackgroundIllustrationQueue>();
        builder.Services.AddSingleton<LegacyArchiveAccess>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<BackgroundAnalysisQueue>());
        builder.Services.AddHostedService(provider => provider.GetRequiredService<BackgroundIllustrationQueue>());
        builder.Services.AddHttpClient(nameof(OpenAiEngravingReader), client => client.Timeout = TimeSpan.FromMinutes(4));
        builder.Services.AddHttpClient(nameof(OpenAiTrophyIllustrator), client => client.Timeout = TimeSpan.FromMinutes(5));

        var app = builder.Build();
        await app.Services.GetRequiredService<AccountStore>().InitializeAsync();
        await app.Services.GetRequiredService<BillingStore>().InitializeAsync();
        var configuredPublicSiteUrl = ResolveConfiguredPublicSiteUrl(builder.Configuration);
        var webRootPath = app.Environment.WebRootPath;
        var marketingDocuments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = Path.Combine(webRootPath, "index.html"),
            ["/uk/how-to-catalogue-trophy-winners/"] = Path.Combine(webRootPath, "uk", "how-to-catalogue-trophy-winners", "index.html"),
            ["/us/how-to-catalog-trophy-winners/"] = Path.Combine(webRootPath, "us", "how-to-catalog-trophy-winners", "index.html")
        };
        var marketingRedirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/index.html"] = "/",
            ["/uk/how-to-catalogue-trophy-winners"] = "/uk/how-to-catalogue-trophy-winners/",
            ["/uk/how-to-catalogue-trophy-winners/index.html"] = "/uk/how-to-catalogue-trophy-winners/",
            ["/us/how-to-catalog-trophy-winners"] = "/us/how-to-catalog-trophy-winners/",
            ["/us/how-to-catalog-trophy-winners/index.html"] = "/us/how-to-catalog-trophy-winners/"
        };

        app.UseResponseCompression();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
            var isHonoursDemo = context.Request.Path.Equals("/honours.html", StringComparison.OrdinalIgnoreCase) &&
                context.Request.Query["demo"] == "1";
            var frameAncestors = isHonoursDemo ? "'self'" : "'none'";
            context.Response.Headers["Content-Security-Policy"] =
                $"default-src 'self'; img-src 'self' data: blob: https://*.google-analytics.com https://www.googletagmanager.com; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; script-src 'self' 'unsafe-inline' https://www.googletagmanager.com; connect-src 'self' https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com; frame-ancestors {frameAncestors}; base-uri 'self'; form-action 'self'";
            if (context.Request.Path.Equals("/archive.html", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/honours") || isHonoursDemo)
            {
                context.Response.Headers["X-Robots-Tag"] = "noindex,nofollow,noarchive";
            }
            await next();
        });

        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? "/";
            if (marketingRedirects.TryGetValue(path, out var cleanPath))
            {
                context.Response.Redirect(cleanPath, permanent: true);
                return;
            }

            var publicSiteUrl = configuredPublicSiteUrl ?? ResolveRequestSiteUrl(context);
            if (marketingDocuments.TryGetValue(path, out var marketingDocumentPath))
            {
                var document = (await File.ReadAllTextAsync(marketingDocumentPath, context.RequestAborted))
                    .Replace("{{PUBLIC_SITE_URL}}", publicSiteUrl, StringComparison.Ordinal);
                var canonicalUrl = path == "/" ? $"{publicSiteUrl}/" : $"{publicSiteUrl}{path}";
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers.Link = $"<{canonicalUrl}>; rel=\"canonical\"";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync(document, context.RequestAborted);
                }
                return;
            }

            if (path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            {
                var robots = $"User-agent: *\nAllow: /\nDisallow: /api/\n\nSitemap: {publicSiteUrl}/sitemap.xml\n";
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.Headers.CacheControl = "public,max-age=3600";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync(robots, context.RequestAborted);
                }
                return;
            }

            if (path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase))
            {
                var sitemap = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
                foreach (var page in marketingDocuments)
                {
                    var location = page.Key == "/" ? $"{publicSiteUrl}/" : $"{publicSiteUrl}{page.Key}";
                    var lastModified = File.GetLastWriteTimeUtc(page.Value).ToString("yyyy-MM-dd");
                    var priority = page.Key == "/" ? "1.0" : "0.8";
                    sitemap.Append($"  <url>\n    <loc>{location}</loc>\n    <lastmod>{lastModified}</lastmod>\n    <changefreq>monthly</changefreq>\n    <priority>{priority}</priority>\n  </url>\n");
                }
                sitemap.Append("</urlset>\n");
                context.Response.ContentType = "application/xml; charset=utf-8";
                context.Response.Headers.CacheControl = "public,max-age=3600";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync(sitemap.ToString(), context.RequestAborted);
                }
                return;
            }

            await next();
        });

        // Older saved catalogues still refer to JPEG copies of the transparent artwork.
        var catalogueImageRedirects = Directory.GetFiles(Path.Combine(webRootPath, "catalogue"), "*.png")
            .ToDictionary(file => $"/catalogue/{Path.GetFileNameWithoutExtension(file)}.jpg",
                file => $"/catalogue/{Path.GetFileName(file)}", StringComparer.OrdinalIgnoreCase);
        app.Use(async (context, next) =>
        {
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                catalogueImageRedirects.TryGetValue(context.Request.Path.Value ?? "", out var transparentImage))
            {
                context.Response.Redirect(transparentImage);
                return;
            }
            await next();
        });

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var extension = Path.GetExtension(context.File.Name);
                context.Context.Response.Headers.CacheControl = extension is ".html" or ".js" or ".css"
                    ? "no-cache"
                    : "public,max-age=604800";
            }
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !RequestSecurity.IsSameOriginMutation(context.Request, builder.Configuration))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "origin_rejected", message = "Reload the archive and try again from its own website." });
                return;
            }
            await next();
        });
        app.UseRateLimiter();
        app.UseAuthentication();

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/api/auth") ||
                context.Request.Path.StartsWithSegments("/api/public") ||
                context.Request.Path == "/api/billing/webhook" ||
                context.Request.Path == "/health")
            {
                await next();
                return;
            }

            var accountId = CurrentAccountId(context);
            if (accountId is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }

            var accounts = context.RequestServices.GetRequiredService<AccountStore>();
            var account = await accounts.GetAccountAsync(accountId, context.RequestAborted);
            if (account is null)
            {
                await context.SignOutAsync(AuthenticationScheme);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }
            context.Items["account"] = account;
            var path = context.Request.Path.Value ?? "";
            var startsAiWork = HttpMethods.IsPost(context.Request.Method) && path.StartsWith("/api/trophies/", StringComparison.OrdinalIgnoreCase) &&
                (path.EndsWith("/images") || path.EndsWith("/analyse") || path.EndsWith("/illustration") || path.EndsWith("/illustration/background"));
            if (startsAiWork && !AccountSecurity.IsEmailVerified(account))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "email_verification_required", message = "Verify your email before starting AI work. Your saved archive remains available." });
                return;
            }
            var club = await accounts.GetClubForAccountAsync(accountId, context.RequestAborted);
            context.Items["club"] = club;

            var isClubRoute = context.Request.Path.StartsWithSegments("/api/club");
            if (!isClubRoute && !accounts.IsClubComplete(club))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new { error = "onboarding_required", message = "Complete your club details and add its logo first." });
                return;
            }

            if (club is null)
            {
                await next();
                return;
            }
            var clubContext = context.RequestServices.GetRequiredService<ClubContextAccessor>();
            using var clubScope = clubContext.Push(club.Id);
            var billing = context.RequestServices.GetRequiredService<BillingStore>();
            billing.EnsureClub(club.Id, AccountSecurity.IsTrustedLegacyAccount(account));
            try { await next(); }
            catch (BillingException exception) when (!context.Response.HasStarted)
            {
                context.Response.StatusCode = exception.StatusCode;
                await context.Response.WriteAsJsonAsync(new { error = exception.Code, message = exception.Message });
            }
        });

        MapHealth(app);
        HonoursEndpoints.Map(app, webRootPath);
        MapAuthentication(app);
        app.MapBillingEndpoints();
        app.MapAccountSecurity();
        MapClub(app);
        MapCatalogue(app);
        MapEvidence(app);
        MapTrophyPhotos(app);
        MapIllustrations(app);
        MapMembers(app);
        MapWinners(app);
        MapExports(app);
        app.MapFallback(() => Results.NotFound());
        await app.RunAsync();
    }

    private static string? ResolveConfiguredPublicSiteUrl(IConfiguration configuration)
    {
        var candidate = configuration["PUBLIC_SITE_URL"] ?? configuration["RENDER_EXTERNAL_URL"];
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string ResolveRequestSiteUrl(HttpContext context)
    {
        var candidate = $"{context.Request.Scheme}://{context.Request.Host}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
            : "http://127.0.0.1";
    }

    private static void MapHealth(WebApplication app)
    {
        app.MapGet("/health", (OpenAiEngravingReader reader, OpenAiTrophyIllustrator illustrator) => Results.Ok(new
        {
            status = "healthy",
            aiConfigured = reader.IsAvailable,
            illustrationConfigured = illustrator.IsAvailable
        }));
    }

    private static void MapAuthentication(WebApplication app)
    {
        app.MapGet("/api/auth/status", async (
            HttpContext context,
            AccountStore accounts,
            BillingStore billing,
            LegacyArchiveAccess legacyAccess,
            OpenAiEngravingReader reader,
            OpenAiTrophyIllustrator illustrator,
            CancellationToken cancellationToken) =>
        {
            var accountId = CurrentAccountId(context);
            var account = accountId is null ? null : await accounts.GetAccountAsync(accountId, cancellationToken);
            var club = account is null ? null : await accounts.GetClubForAccountAsync(account.Id, cancellationToken);
            return Results.Ok(AuthPayload(account, club, accounts, reader, illustrator, legacyAccess, billing));
        });

        app.MapPost("/api/auth/signup", async (
            HttpContext context,
            SignupInput input,
            AccountStore accounts,
            BillingStore billing,
            TransactionalEmail email,
            LegacyArchiveAccess legacyAccess,
            OpenAiEngravingReader reader,
            OpenAiTrophyIllustrator illustrator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!email.IsAvailable) return Results.Json(new { error = "registration_unavailable", message = "Registration is temporarily unavailable while email delivery is being configured. Existing accounts can still sign in." }, statusCode: 503);
                var account = await accounts.CreateAccountAsync(input, cancellationToken);
                var verificationEmailSent = await AccountSecurity.IssueVerificationAsync(account, accounts, email, cancellationToken);
                await SignInAccountAsync(context, account);
                return Results.Ok(AuthPayload(account, null, accounts, reader, illustrator, legacyAccess, billing, verificationEmailSent));
            }
            catch (AccountStoreException exception)
            {
                return Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.Code == "email_in_use" ? 409 : 400);
            }
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/login", async (
            HttpContext context,
            LoginInput input,
            AccountStore accounts,
            BillingStore billing,
            LegacyArchiveAccess legacyAccess,
            OpenAiEngravingReader reader,
            OpenAiTrophyIllustrator illustrator,
            CancellationToken cancellationToken) =>
        {
            var account = await accounts.AuthenticateAsync(input, cancellationToken);
            if (account is null)
            {
                await Task.Delay(Random.Shared.Next(350, 750), cancellationToken);
                return Results.Json(new { error = "incorrect_credentials", message = "That email and password combination was not recognised." }, statusCode: 401);
            }
            await SignInAccountAsync(context, account);
            var club = await accounts.GetClubForAccountAsync(account.Id, cancellationToken);
            return Results.Ok(AuthPayload(account, club, accounts, reader, illustrator, legacyAccess, billing));
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/original-archive", async (
            HttpContext context,
            LegacyLoginInput input,
            AccountStore accounts,
            BillingStore billing,
            LegacyArchiveAccess legacyAccess,
            OpenAiEngravingReader reader,
            OpenAiTrophyIllustrator illustrator,
            CancellationToken cancellationToken) =>
        {
            if (!legacyAccess.IsAvailable)
                return Results.NotFound(new { error = "original_archive_unavailable", message = "No original archive is available on this installation." });
            if (!legacyAccess.PasswordMatches(input.Password))
            {
                await Task.Delay(Random.Shared.Next(350, 750), cancellationToken);
                return Results.Json(new { error = "incorrect_password", message = "That is not the original archive password." }, statusCode: 401);
            }

            var account = await accounts.OpenLegacyArchiveAsync(input.Password ?? string.Empty, cancellationToken);
            var club = await accounts.GetClubForAccountAsync(account.Id, cancellationToken);
            await SignInAccountAsync(context, account);
            return Results.Ok(AuthPayload(account, club, accounts, reader, illustrator, legacyAccess, billing));
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthenticationScheme);
            return Results.Ok(new { authenticated = false });
        });
    }

    private static void MapClub(WebApplication app)
    {
        app.MapGet("/api/club", async (HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var club = await accounts.GetClubForAccountAsync(CurrentAccountId(context)!, cancellationToken);
            return club is null ? Results.NotFound() : Results.Ok(new { club = ClubPayload(club, accounts), complete = accounts.IsClubComplete(club) });
        });

        app.MapPut("/api/club", async (HttpContext context, ClubInput input, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            if (!AccountSecurity.IsOwner(context.Items["account"] as AccountRecord)) return Results.Json(new { error = "owner_required", message = "Only the archive owner can change club details." }, statusCode: 403);
            try
            {
                var club = await accounts.UpsertClubAsync(CurrentAccountId(context)!, input, cancellationToken);
                return Results.Ok(new { club = ClubPayload(club, accounts), complete = accounts.IsClubComplete(club) });
            }
            catch (AccountStoreException exception)
            {
                return Results.BadRequest(new { error = exception.Code, message = exception.Message });
            }
        });

        app.MapPost("/api/club/logo", async (HttpContext context, HttpRequest request, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            if (!AccountSecurity.IsOwner(context.Items["account"] as AccountRecord)) return Results.Json(new { error = "owner_required", message = "Only the archive owner can change club details." }, statusCode: 403);
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Choose a club logo first." });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("logo") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Choose a club logo first." });
            if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { error = "Keep the club logo below 5 MB." });
            if (!AcceptedImageTypes.Contains(file.ContentType)) return Results.BadRequest(new { error = "Use a JPEG, PNG or WebP club logo." });
            try
            {
                await using var stream = file.OpenReadStream();
                var club = await accounts.SaveClubLogoAsync(CurrentAccountId(context)!, file.ContentType, stream, cancellationToken);
                return Results.Ok(new { club = ClubPayload(club, accounts), complete = accounts.IsClubComplete(club) });
            }
            catch (AccountStoreException exception)
            {
                return Results.BadRequest(new { error = exception.Code, message = exception.Message });
            }
        }).DisableAntiforgery();

        app.MapGet("/api/club/logo", async (HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var logo = await accounts.GetLogoAsync(CurrentAccountId(context)!, cancellationToken);
            return logo is null ? Results.NotFound() : Results.File(logo.Value.Path, logo.Value.ContentType, enableRangeProcessing: true);
        });
    }

    private static void MapCatalogue(WebApplication app)
    {
        app.MapGet("/api/trophies", async (CatalogueStore store, OpenAiEngravingReader reader, OpenAiTrophyIllustrator illustrator, CancellationToken cancellationToken) =>
        {
            var items = await store.GetSummariesAsync(cancellationToken);
            return Results.Ok(new
            {
                items,
                totals = new
                {
                    all = items.Count,
                    notStarted = items.Count(item => item.Status == TrophyStatuses.NotStarted),
                    inProgress = items.Count(item => item.Status == TrophyStatuses.InProgress),
                    complete = items.Count(item => item.Status == TrophyStatuses.Complete),
                    needsReview = items.Count(item => item.NeedsReviewCount > 0)
                },
                aiConfigured = reader.IsAvailable,
                illustrationConfigured = illustrator.IsAvailable
            });
        });

        app.MapPost("/api/trophies", async (TrophyCreateInput input, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var error = ValidateTrophy(input);
            if (error is not null) return Results.BadRequest(new { error });
            try
            {
                var trophy = await store.CreateTrophyAsync(input, cancellationToken);
                return Results.Created($"/api/trophies/{trophy.Id}", new { trophy, missingYears = Array.Empty<int>() });
            }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        });

        app.MapGet("/api/trophies/{id}", async (
            string id,
            CatalogueStore store,
            MemberMatchingCoordinator matching,
            CancellationToken cancellationToken) =>
        {
            var trophy = await matching.RefreshTrophyAsync(id, cancellationToken)
                ?? await store.GetTrophyAsync(id, cancellationToken);
            return trophy is null ? Results.NotFound() : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPut("/api/trophies/{id}/timeline", async (string id, TimelineInput input, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            if (!ValidTimeline(input)) return Results.BadRequest(new { error = "Choose years from 1800 to 2200, with the first year before the last." });
            var trophy = await store.UpdateTimelineAsync(id, input, cancellationToken);
            return trophy is null ? Results.NotFound() : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPut("/api/trophies/{id}/division", async (
            string id,
            TrophyDivisionInput input,
            CatalogueStore store,
            MemberMatchingCoordinator matching,
            CancellationToken cancellationToken) =>
        {
            var trophy = await store.UpdateDivisionAsync(id, input, cancellationToken);
            if (trophy is null) return Results.NotFound();
            trophy = await matching.RefreshTrophyAsync(id, cancellationToken) ?? trophy;
            return Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPut("/api/trophies/{id}/award-format", async (
            string id,
            TrophyAwardFormatInput input,
            CatalogueStore store,
            CancellationToken cancellationToken) =>
        {
            if (!AwardFormats.IsValid(input.AwardFormat))
                return Results.BadRequest(new { error = "Choose Detect from photos, Individual winner, or Team of players." });
            var trophy = await store.UpdateAwardFormatAsync(id, input, cancellationToken);
            return trophy is null
                ? Results.NotFound()
                : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPut("/api/trophies/{id}/engraving-instructions", async (
            string id,
            TrophyEngravingInstructionsInput input,
            CatalogueStore store,
            CancellationToken cancellationToken) =>
        {
            if ((input.Instructions?.Trim().Length ?? 0) > 2000)
                return Results.BadRequest(new { error = "Keep the source-reading instructions under 2,000 characters." });
            var trophy = await store.UpdateEngravingInstructionsAsync(id, input, cancellationToken);
            return trophy is null
                ? Results.NotFound()
                : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPost("/api/trophies/{id}/complete", async (string id, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var trophy = await store.MarkCompleteAsync(id, cancellationToken);
            return trophy is null ? Results.NotFound() : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });
    }

    private static void MapEvidence(WebApplication app)
    {
        app.MapPost("/api/trophies/{id}/images", async (
            string id,
            HttpRequest request,
            CatalogueStore store,
            OpenAiEngravingReader reader,
            BackgroundAnalysisQueue analysisQueue,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "An image upload is required." });
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            var form = await request.ReadFormAsync(cancellationToken);
            var files = form.Files.ToList();
            var kind = form["kind"].ToString() == EvidenceKinds.Rubbing ? EvidenceKinds.Rubbing : EvidenceKinds.Photo;
            if (files.Count == 0) return Results.BadRequest(new { error = "Choose one or more winner-record images first." });
            if (files.Count > 30) return Results.BadRequest(new { error = "Upload no more than 30 images at once." });
            if (files.Any(file => file.Length == 0)) return Results.BadRequest(new { error = "One of those images is empty. Remove it and try again." });
            if (files.Any(file => file.Length > 12 * 1024 * 1024)) return Results.BadRequest(new { error = "Each image must be no larger than 12 MB." });
            if (files.Sum(file => file.Length) > 55 * 1024 * 1024) return Results.BadRequest(new { error = "That batch is larger than 55 MB. Upload it in two groups." });
            if (files.Any(file => !AcceptedImageTypes.Contains(file.ContentType))) return Results.BadRequest(new { error = "Use JPEG, PNG or WebP images." });

            var billing = request.HttpContext.RequestServices.GetRequiredService<BillingStore>();
            var clubId = request.HttpContext.RequestServices.GetRequiredService<ClubContextAccessor>().RequireClubId();
            billing.CheckPhotoAllowance(clubId, id, trophy.Evidence.Count + trophy.TrophyPhotos.Count + files.Count);

            var addedEvidence = new List<EvidenceImage>();
            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                var evidence = await store.AddEvidenceAsync(id, file.FileName, file.ContentType, kind, stream, cancellationToken);
                if (evidence is null) return Results.NotFound();
                addedEvidence.Add(evidence);
            }

            trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            AnalysisJobSnapshot analysis;
            if (reader.IsAvailable) analysis = analysisQueue.Enqueue(id, trophy.Evidence.Count);
            else
            {
                const string message = "Add OPENAI_API_KEY to enable the winner-record reader.";
                foreach (var evidence in addedEvidence)
                    await store.SetEvidenceProcessingAsync(id, evidence.Id, ProcessingStates.Failed, message, cancellationToken);
                trophy = await store.GetTrophyAsync(id, cancellationToken) ?? trophy;
                analysis = new AnalysisJobSnapshot("failed", message, DateTimeOffset.UtcNow, trophy.Evidence.Count);
            }

            return Results.Accepted($"/api/trophies/{id}/analysis-status", new
            {
                trophy,
                missingYears = CatalogueStore.MissingYears(trophy),
                addedEvidence,
                analysis
            });
        }).DisableAntiforgery();

        app.MapPost("/api/trophies/{id}/analyse", async (string id, CatalogueStore store, OpenAiEngravingReader reader, BackgroundAnalysisQueue queue, CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            if (trophy.Evidence.Count == 0) return Results.BadRequest(new { error = "Add at least one image first." });
            if (!reader.IsAvailable) return Results.Json(new { error = "analysis_failed", message = "Add OPENAI_API_KEY to enable the winner-record reader." }, statusCode: 503);
            return Results.Accepted($"/api/trophies/{id}/analysis-status", new { analysis = queue.EnqueueNow(id, trophy.Evidence.Count) });
        });

        app.MapGet("/api/trophies/{id}/analysis-status", async (string id, CatalogueStore store, BackgroundAnalysisQueue queue, CancellationToken cancellationToken) =>
            await store.GetTrophyAsync(id, cancellationToken) is null ? Results.NotFound() : Results.Ok(new { analysis = queue.GetStatus(id) }));

        app.MapGet("/api/trophies/{id}/images/{imageId}", async (string id, string imageId, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            var evidence = trophy?.Evidence.FirstOrDefault(item => item.Id == imageId);
            var path = await store.GetEvidencePathAsync(id, imageId, cancellationToken);
            return evidence is null || path is null ? Results.NotFound() : Results.File(path, evidence.ContentType, enableRangeProcessing: true);
        });

        app.MapDelete("/api/trophies/{id}/images/{imageId}", async (string id, string imageId, CatalogueStore store, CancellationToken cancellationToken) =>
            await store.DeleteEvidenceAsync(id, imageId, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapTrophyPhotos(WebApplication app)
    {
        app.MapPost("/api/trophies/{id}/trophy-photos", async (
            string id,
            HttpRequest request,
            CatalogueStore store,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "A trophy photograph upload is required." });
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            var form = await request.ReadFormAsync(cancellationToken);
            var files = form.Files.ToList();
            if (files.Count == 0) return Results.BadRequest(new { error = "Choose one or more clear photographs of the whole trophy." });
            if (files.Count > 12) return Results.BadRequest(new { error = "Upload no more than 12 trophy photographs at once." });
            if (files.Any(file => file.Length == 0)) return Results.BadRequest(new { error = "One of those photographs is empty. Remove it and try again." });
            if (files.Any(file => file.Length > 12 * 1024 * 1024)) return Results.BadRequest(new { error = "Each photograph must be no larger than 12 MB." });
            if (files.Sum(file => file.Length) > 55 * 1024 * 1024) return Results.BadRequest(new { error = "That batch is larger than 55 MB. Upload it in two groups." });
            if (files.Any(file => !AcceptedImageTypes.Contains(file.ContentType))) return Results.BadRequest(new { error = "Use JPEG, PNG or WebP photographs." });

            var billing = request.HttpContext.RequestServices.GetRequiredService<BillingStore>();
            var clubId = request.HttpContext.RequestServices.GetRequiredService<ClubContextAccessor>().RequireClubId();
            billing.CheckPhotoAllowance(clubId, id, trophy.Evidence.Count + trophy.TrophyPhotos.Count + files.Count);

            var addedPhotos = new List<EvidenceImage>();
            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                var photo = await store.AddTrophyPhotoAsync(id, file.FileName, file.ContentType, stream, cancellationToken);
                if (photo is null) return Results.NotFound();
                addedPhotos.Add(photo);
            }

            trophy = await store.GetTrophyAsync(id, cancellationToken);
            return Results.Ok(new { trophy, addedPhotos });
        }).DisableAntiforgery();

        app.MapGet("/api/trophies/{id}/trophy-photos/{photoId}", async (string id, string photoId, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            var photo = trophy?.TrophyPhotos.FirstOrDefault(item => item.Id == photoId);
            var path = await store.GetTrophyPhotoPathAsync(id, photoId, cancellationToken);
            return photo is null || path is null ? Results.NotFound() : Results.File(path, photo.ContentType, enableRangeProcessing: true);
        });

        app.MapDelete("/api/trophies/{id}/trophy-photos/{photoId}", async (string id, string photoId, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            if (!await store.DeleteTrophyPhotoAsync(id, photoId, cancellationToken)) return Results.NotFound();
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            return Results.Ok(new { trophy });
        });
    }
    private static void MapIllustrations(WebApplication app)
    {
        app.MapPost("/api/trophies/{id}/illustration/background", async (
            string id,
            CatalogueStore store,
            OpenAiTrophyIllustrator illustrator,
            BackgroundIllustrationQueue queue,
            CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            var references = await store.GetTrophyPhotoFilesAsync(id, cancellationToken);
            if (references.Count == 0)
                return Results.BadRequest(new { error = "Add at least one clear photograph of the trophy first." });
            if (!illustrator.IsAvailable)
                return Results.Json(new { error = "illustration_unavailable", message = "Add OPENAI_API_KEY to enable trophy illustrations." }, statusCode: 503);

            var job = queue.Enqueue(id);
            await store.SetIllustrationStatusAsync(id, IllustrationStates.Processing, "Illustration queued. The trophy is ready to use while it is generated.", cancellationToken);
            trophy = await store.GetTrophyAsync(id, cancellationToken);
            return Results.Accepted($"/api/trophies/{id}/illustration/status", new { trophy, illustration = job });
        });

        app.MapGet("/api/trophies/{id}/illustration/status", async (
            string id,
            CatalogueStore store,
            BackgroundIllustrationQueue queue,
            CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            return trophy is null ? Results.NotFound() : Results.Ok(new { trophy, illustration = queue.GetStatus(id) });
        });

        app.MapPost("/api/trophies/{id}/illustration", async (
            string id, CatalogueStore store, OpenAiTrophyIllustrator illustrator,
            BackgroundIllustrationQueue queue, CancellationToken cancellationToken) =>
        {
            var trophy = await store.GetTrophyAsync(id, cancellationToken);
            if (trophy is null) return Results.NotFound();
            if (trophy.TrophyPhotos.Count == 0) return Results.BadRequest(new { error = "Add at least one clear photograph of the trophy first." });
            if (!illustrator.IsAvailable) return Results.Json(new { error = "illustration_unavailable", message = "Trophy illustration is not configured." }, statusCode: 503);
            var job = queue.Enqueue(id);
            return Results.Accepted($"/api/trophies/{id}/illustration/status", new { trophy, illustration = job });
        });

        app.MapGet("/api/trophies/{id}/illustration", async (string id, CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var path = await store.GetIllustrationPathAsync(id, cancellationToken);
            return path is null ? Results.NotFound() : Results.File(path, "image/png", enableRangeProcessing: true);
        });
    }

    private static void MapMembers(WebApplication app)
    {
        app.MapGet("/api/members", async (MemberDirectoryStore directory, CancellationToken cancellationToken) =>
            Results.Ok(new { directory = await directory.GetSummaryAsync(cancellationToken) }));

        app.MapPost("/api/members/import", async (HttpRequest request, MemberDirectoryStore directory, MemberMatchingCoordinator matching, CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Choose a CSV, XML or XLSX member export." });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Choose a CSV, XML or XLSX member export." });
            if (file.Length > 15 * 1024 * 1024) return Results.BadRequest(new { error = "Keep the member export below 15 MB." });
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not ".csv" and not ".tsv" and not ".xlsx" and not ".xml") return Results.BadRequest(new { error = "Use a CSV, TSV, XML or XLSX file." });
            try
            {
                await using var stream = file.OpenReadStream();
                var result = await directory.ImportAsync(file.FileName, stream, cancellationToken);
                await matching.RefreshAllAsync(cancellationToken);
                return Results.Ok(new { result, directory = await directory.GetSummaryAsync(cancellationToken) });
            }
            catch (MemberImportException exception) { return Results.BadRequest(new { error = exception.Message }); }
        }).DisableAntiforgery();

        app.MapPost("/api/members/rematch/{trophyId}", async (string trophyId, MemberMatchingCoordinator matching, CancellationToken cancellationToken) =>
        {
            var trophy = await matching.RefreshTrophyAsync(trophyId, cancellationToken);
            return trophy is null ? Results.NotFound() : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapGet("/api/trophies/{trophyId}/winners/{winnerId}/member-candidates", async (
            string trophyId,
            string winnerId,
            MemberMatchingCoordinator matching,
            CancellationToken cancellationToken) =>
        {
            var candidates = await matching.GetCandidatesAsync(trophyId, winnerId, cancellationToken);
            return candidates is null ? Results.NotFound() : Results.Ok(new { candidates });
        });

        app.MapPut("/api/trophies/{trophyId}/winners/{winnerId}/member-match", async (
            string trophyId,
            string winnerId,
            MemberMatchSelectionInput input,
            MemberMatchingCoordinator matching,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.MemberId))
                return Results.BadRequest(new { error = "Choose a member first." });
            var trophy = await matching.SelectMemberAsync(trophyId, winnerId, input.MemberId, cancellationToken);
            return trophy is null
                ? Results.NotFound(new { error = "That member match is no longer available." })
                : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });

        app.MapPost("/api/trophies/{trophyId}/winners/{winnerId}/member-match/manual", async (
            string trophyId,
            string winnerId,
            ManualMemberInput input,
            MemberDirectoryStore directory,
            MemberMatchingCoordinator matching,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var trophy = await matching.AddAndSelectMemberAsync(trophyId, winnerId, input, cancellationToken);
                return trophy is null
                    ? Results.NotFound(new { error = "That winner is no longer available." })
                    : Results.Ok(new
                    {
                        trophy,
                        missingYears = CatalogueStore.MissingYears(trophy),
                        directory = await directory.GetSummaryAsync(cancellationToken)
                    });
            }
            catch (MemberImportException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/trophies/{trophyId}/winners/{winnerId}/member-match", async (
            string trophyId,
            string winnerId,
            CatalogueStore catalogue,
            CancellationToken cancellationToken) =>
        {
            var trophy = await catalogue.KeepWinnerUnmatchedAsync(trophyId, winnerId, cancellationToken);
            return trophy is null
                ? Results.NotFound()
                : Results.Ok(new { trophy, missingYears = CatalogueStore.MissingYears(trophy) });
        });
        app.MapDelete("/api/members", async (MemberDirectoryStore directory, CatalogueStore catalogue, CancellationToken cancellationToken) =>
        {
            await directory.ClearAsync(cancellationToken);
            await catalogue.ClearMemberMatchesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapWinners(WebApplication app)
    {
        app.MapPost("/api/trophies/{id}/winners", async (string id, WinnerInput input, CatalogueStore store, MemberMatchingCoordinator matching, CancellationToken cancellationToken) =>
        {
            var error = ValidateWinner(input);
            if (error is not null) return Results.BadRequest(new { error });
            var winner = await store.AddWinnerAsync(id, input, cancellationToken);
            if (winner is null) return Results.NotFound();
            var trophy = await matching.RefreshTrophyAsync(id, cancellationToken);
            var matched = trophy?.Winners.FirstOrDefault(item => item.Id == winner.Id) ?? winner;
            return Results.Created($"/api/trophies/{id}/winners/{winner.Id}", matched);
        });

        app.MapPut("/api/trophies/{id}/winners/{winnerId}", async (string id, string winnerId, WinnerInput input, CatalogueStore store, MemberMatchingCoordinator matching, CancellationToken cancellationToken) =>
        {
            var error = ValidateWinner(input);
            if (error is not null) return Results.BadRequest(new { error });
            var winner = await store.UpdateWinnerAsync(id, winnerId, input, cancellationToken);
            if (winner is null) return Results.NotFound();
            var trophy = await matching.RefreshTrophyAsync(id, cancellationToken);
            return Results.Ok(trophy?.Winners.FirstOrDefault(item => item.Id == winnerId) ?? winner);
        });

        app.MapDelete("/api/trophies/{id}/winners/{winnerId}", async (string id, string winnerId, CatalogueStore store, CancellationToken cancellationToken) =>
            await store.DeleteWinnerAsync(id, winnerId, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapExports(WebApplication app)
    {
        app.MapGet("/api/export.csv", async (CatalogueStore store, CancellationToken cancellationToken) =>
        {
            var summaries = await store.GetSummariesAsync(cancellationToken);
            var csv = new StringBuilder("Trophy code,Trophy name,Year,Winner,Description,AI reading notes,Review status,Source,Matched member,Membership number,Birth year,Joining year,Match confidence,Match reason\r\n");
            foreach (var summary in summaries)
            {
                var trophy = await store.GetTrophyAsync(summary.Id, cancellationToken);
                if (trophy is null) continue;
                foreach (var winner in trophy.Winners.OrderBy(item => item.Year).ThenBy(item => item.Name))
                {
                    csv.AppendLine(string.Join(',', new[]
                    {
                        Csv(trophy.Id), Csv(trophy.Name), winner.Year.ToString(), Csv(winner.Name),
                        Csv(winner.Description ?? string.Empty), Csv(winner.ExtractionNotes ?? string.Empty),
                        Csv(winner.ReviewState), Csv(winner.Source),
                        Csv(winner.MemberMatch?.MemberName ?? string.Empty), Csv(winner.MemberMatch?.MembershipNumber ?? string.Empty),
                        winner.MemberMatch?.BirthYear?.ToString() ?? string.Empty,
                        winner.MemberMatch?.JoinYear?.ToString() ?? string.Empty,
                        winner.MemberMatch is null ? string.Empty : Math.Round(winner.MemberMatch.Confidence * 100).ToString(),
                        Csv(winner.MemberMatch?.Explanation ?? string.Empty)
                    }));
                }
            }
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"trophy-archive-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        });
    }

    private static Task SignInAccountAsync(HttpContext context, AccountRecord account) => AccountSecurity.SignInAsync(context, account);

    private static object AuthPayload(
        AccountRecord? account,
        ClubRecord? club,
        AccountStore accounts,
        OpenAiEngravingReader reader,
        OpenAiTrophyIllustrator illustrator,
        LegacyArchiveAccess legacyAccess,
        BillingStore billing, bool? verificationEmailSent = null) => new
    {
        authenticated = account is not null,
        onboardingRequired = account is not null && !accounts.IsClubComplete(club),
        user = account is null ? null : new { account.Id, account.DisplayName, account.Email, account.Role, emailVerified = AccountSecurity.IsEmailVerified(account) },
        verificationEmailSent,
        notice = verificationEmailSent == false ? "Your account has been created, but the verification email could not be sent. Open account security to resend it." : null,
        balance = account is null ? null : AccountBalance(account, billing),
        club = ClubPayload(club, accounts),
        aiConfigured = reader.IsAvailable,
        illustrationConfigured = illustrator.IsAvailable,
        originalArchiveAvailable = legacyAccess.IsAvailable,
        originalArchivePasswordRequired = legacyAccess.PasswordRequired,
        model = reader.Model,
        illustrationModel = illustrator.Model
    };

    private static object AccountBalance(AccountRecord account, BillingStore billing)
    {
        if (account.ClubId is null) return new { trophyCredits = 1L, unlimited = false, planCode = "free", used = 0L, reserved = 0L };
        billing.EnsureClub(account.ClubId, AccountSecurity.IsTrustedLegacyAccount(account));
        var balance = billing.Balance(account.ClubId);
        return new { trophyCredits = balance.Available, unlimited = balance.Unlimited, planCode = balance.Unlimited ? "unlimited" : "credits", used = balance.Used, reserved = balance.Reserved };
    }

    private static object? ClubPayload(ClubRecord? club, AccountStore accounts) => club is null ? null : new
    {
        club.Id,
        club.Name,
        club.Sport,
        club.Country,
        club.Website,
        logoUrl = AccountStore.LogoUrl(club),
        complete = accounts.IsClubComplete(club)
    };

    private static string? CurrentAccountId(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string? ValidateTrophy(TrophyCreateInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length < 2) return "Enter the trophy name.";
        if (input.Name.Trim().Length > 160) return "Keep the trophy name under 160 characters.";
        if (!string.IsNullOrWhiteSpace(input.Code) && input.Code.Trim().Length > 24) return "Keep the trophy code under 24 characters.";
        if (!string.IsNullOrWhiteSpace(input.Category) && input.Category.Trim().Length > 80) return "Keep the category under 80 characters.";
        return null;
    }

    private static string? ValidateWinner(WinnerInput input)
    {
        if (input.Year is < 1800 or > 2200) return "Enter a year from 1800 to 2200.";
        if (string.IsNullOrWhiteSpace(input.Name)) return "Enter the winner's name.";
        if (input.Name.Trim().Length > 200) return "Keep the winner's name under 200 characters.";
        if (((input.Description ?? input.Notes)?.Trim().Length ?? 0) > 500) return "Keep the winner description under 500 characters.";
        return null;
    }

    private static bool ValidTimeline(TimelineInput input)
    {
        if (input.StartYear is < 1800 or > 2200 || input.EndYear is < 1800 or > 2200) return false;
        return !input.StartYear.HasValue || !input.EndYear.HasValue || (input.StartYear <= input.EndYear && input.EndYear - input.StartYear <= 250);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
