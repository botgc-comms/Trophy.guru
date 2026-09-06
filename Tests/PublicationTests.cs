using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class PublicationTests
{
    [Fact]
    public async Task ConfirmingAndPreviewingNeverPublishesAndDoesNotCreatePublicationState()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.False((await fixture.Store.GetAsync("legacy")).IsPublic);
        Assert.Null(await fixture.Store.GetPublicAssetAsync("legacy", "logo"));
        var preview = await fixture.Store.PreviewAsync("legacy", fixture.Options());
        Assert.Equal("A. Smith", Assert.Single(Assert.Single(preview.Snapshot.Trophies).Winners).Name);
        Assert.False((await fixture.Store.GetAsync("legacy")).IsPublic);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "honours-publication")));
    }

    [Theory]
    [InlineData(MemberMatchStates.Strong)]
    [InlineData(MemberMatchStates.Possible)]
    public async Task AutomaticMatchesCannotSupplyPublicFullNames(string matchState)
    {
        using var fixture = await Fixture.CreateAsync();
        using (fixture.Context.Push("legacy"))
            await fixture.Catalogue.SetMemberMatchAsync("CUP", "winner", Match("Alice Private", false, matchState));
        var options = fixture.Options();
        options.NamePolicy = "approved-identities";
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        var publicWinner = Assert.Single(Assert.Single(preview.Snapshot.Trophies).Winners);
        Assert.Equal("A. Smith", publicWinner.Name);
        Assert.Null(publicWinner.Description);
        var json = JsonSerializer.Serialize(preview.Snapshot);
        Assert.DoesNotContain("Alice Private", json);
        Assert.DoesNotContain("membership-secret", json);
        Assert.DoesNotContain("BirthYear", json);
    }

    [Fact]
    public async Task ManualIdentityRequiresPublicNamePolicyAndLaterMatchingCannotChangeSnapshot()
    {
        using var fixture = await Fixture.CreateAsync();
        using (fixture.Context.Push("legacy"))
            await fixture.Catalogue.SetMemberMatchAsync("CUP", "winner", Match("Alice Smith", true));
        var inscription = await fixture.Store.PreviewAsync("legacy", fixture.Options());
        Assert.Equal("A. Smith", inscription.Snapshot.Trophies[0].Winners[0].Name);
        var options = fixture.Options();
        options.NamePolicy = "approved-identities";
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        Assert.Equal("Alice Smith", preview.Snapshot.Trophies[0].Winners[0].Name);
        await fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, true));
        using (fixture.Context.Push("legacy"))
            await fixture.Catalogue.SetMemberMatchAsync("CUP", "winner", Match("Another Identity", true));
        var published = await fixture.Store.GetAsync("legacy");
        Assert.Equal("Alice Smith", published.Snapshot!.Trophies[0].Winners[0].Name);
    }

    [Fact]
    public async Task ChangedNamesOrOptionsInvalidateTheOwnersReviewedPreview()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = fixture.Options();
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        using (fixture.Context.Push("legacy"))
            await fixture.Catalogue.UpdateWinnerAsync("CUP", "winner", new(2020, "Different Winner", ReviewStates.Confirmed, "Private note"));
        var error = await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, true)));
        Assert.Equal("preview_changed", error.Code);
        Assert.False((await fixture.Store.GetAsync("legacy")).IsPublic);
    }

    [Fact]
    public async Task UnconfirmedAndJuniorRecordsCannotBeAddedWithoutExplicitEligibility()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = fixture.Options();
        options.SelectedWinnerKeys.Add("CUP:unconfirmed");
        Assert.Equal("selection_changed", (await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PreviewAsync("legacy", options))).Code);
        options.SelectedWinnerKeys = ["JUNIOR:child"];
        Assert.Equal("selection_changed", (await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PreviewAsync("legacy", options))).Code);
        options.IncludeJuniorTrophies = true;
        Assert.Equal(1, (await fixture.Store.PreviewAsync("legacy", options)).Snapshot.Summary.Honours);
    }

    [Fact]
    public async Task PublicImagesAreFrozenAndWithdrawalGatesThemWithoutDeletingArchive()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = fixture.Options();
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        await fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, true));
        var asset = await fixture.Store.GetPublicAssetAsync("legacy", "logo");
        Assert.NotNull(asset);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(asset.Value.Path));
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "brand", "logo.png"), [9, 9, 9]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(asset.Value.Path));
        var withdrawn = await fixture.Store.WithdrawAsync("legacy", "owner");
        Assert.False(withdrawn.IsPublic);
        Assert.Null(await fixture.Store.GetPublicAssetAsync("legacy", "logo"));
        using (fixture.Context.Push("legacy"))
            Assert.Equal(2, (await fixture.Catalogue.GetTrophyAsync("CUP"))!.Winners.Count);
        Assert.Equal("withdrawn", withdrawn.Audit.Last().Action);
    }

    [Fact]
    public async Task ChangedImageRequiresAnotherPreviewAndApprovalIsRequired()
    {
        using var fixture = await Fixture.CreateAsync();
        var options = fixture.Options();
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        Assert.Equal("publication_approval_required", (await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, false)))).Code);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "brand", "logo.png"), [8, 8, 8]);
        Assert.Equal("preview_changed", (await Assert.ThrowsAsync<PublicationException>(() => fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, true)))).Code);
    }

    [Theory]
    [InlineData("https://club.example/path")]
    [InlineData("https://*.example")]
    [InlineData("https://club.example?token=bad")]
    [InlineData("https://person:secret@club.example")]
    [InlineData("http://club.example")]
    [InlineData("https://club.example; frame-ancestors *")]
    public void EmbedOriginsRejectPathsWildcardsCredentialsAndCspInjection(string origin)
    {
        Assert.Throws<PublicationException>(() => HonoursPublicationStore.NormalizeOptions(new() { AllowedEmbedOrigins = [origin] }));
    }

    [Fact]
    public void EmbedOriginsAreExactCanonicalOriginsAndPrivateIsDefault()
    {
        var options = HonoursPublicationStore.NormalizeOptions(new() { AllowedEmbedOrigins = ["https://www.club.example/", "https://www.club.example", "http://localhost:5189"] });
        Assert.Equal(2, options.AllowedEmbedOrigins.Count);
        Assert.Contains("https://www.club.example", options.AllowedEmbedOrigins);
        Assert.False(new HonoursPublication().IsPublic);
        Assert.False(options.IncludeDescriptions);
        Assert.False(options.IncludeJuniorTrophies);
    }

    [Fact]
    public async Task PublicHtmlJsonAndAssetsShareTheSameGateAndEmbedCspAllowsOnlyConfiguredOrigins()
    {
        using var fixture = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "wwwroot", "honours.html"), "<html><head><script src=\"/analytics.js?v=1\" defer></script><link href=\"https://fonts.googleapis.com/css\" rel=\"stylesheet\"></head><body>Honours</body></html>");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = fixture.Root, WebRootPath = Path.Combine(fixture.Root, "wwwroot"), EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(fixture.Store);
        builder.Services.AddSingleton(fixture.Accounts);
        builder.Services.AddSingleton(fixture.Catalogue);
        await using var app = builder.Build();
        HonoursEndpoints.Map(app, Path.Combine(fixture.Root, "wwwroot"));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var protectedUrls = new[] { "/honours/legacy", "/embed/legacy", "/api/public/clubs/legacy/honours", "/api/public/clubs/legacy/logo", "/api/public/clubs/legacy/trophies/CUP/illustration" };
        foreach (var url in protectedUrls) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        var options = fixture.Options();
        options.AllowedEmbedOrigins = ["https://www.fictional-club.example"];
        var preview = await fixture.Store.PreviewAsync("legacy", options);
        await fixture.Store.PublishAsync("legacy", "owner", new(options, preview.Fingerprint, true));
        foreach (var url in protectedUrls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        var embed = await client.GetAsync("/embed/legacy");
        var csp = embed.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self' https://www.fictional-club.example;", csp);
        Assert.DoesNotContain("analytics.js", await embed.Content.ReadAsStringAsync());
        Assert.DoesNotContain("fonts.googleapis.com", await embed.Content.ReadAsStringAsync());
        var hosted = await client.GetAsync("/honours/legacy");
        Assert.Contains("frame-ancestors 'none';", hosted.Headers.GetValues("Content-Security-Policy").Single());
        await fixture.Store.WithdrawAsync("legacy", "owner");
        foreach (var url in protectedUrls) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        await app.StopAsync();
    }

    private static MemberMatchRecord Match(string name, bool manual, string state = MemberMatchStates.Strong) => new()
    {
        MemberId = "member-private", MemberName = name, MembershipNumber = "membership-secret", BirthYear = 1980,
        JoinYear = 2000, State = state, ManuallySelected = manual, Explanation = "Private matching metadata"
    };

    private sealed class Fixture : IDisposable
    {
        public required string Root { get; init; }
        public required ClubContextAccessor Context { get; init; }
        public required CatalogueStore Catalogue { get; init; }
        public required AccountStore Accounts { get; init; }
        public required HonoursPublicationStore Store { get; init; }
        public HonoursPublicationOptions Options() => new() { SelectedWinnerKeys = ["CUP:winner"] };
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "trophy-publication-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "brand"));
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "catalogue"));
            await File.WriteAllBytesAsync(Path.Combine(root, "brand", "logo.png"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(Path.Combine(root, "wwwroot", "catalogue", "test.png"), [4, 3, 2, 1]);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var identity = new IdentityState
            {
                Clubs = [new() { Id = "legacy", Name = "Fictional Test Club", Sport = "Golf", Country = "United Kingdom", LogoStoredName = "logo.png", LogoContentType = "image/png" }]
            };
            await File.WriteAllTextAsync(Path.Combine(root, "identity.json"), JsonSerializer.Serialize(identity, options));
            var state = new CatalogueState
            {
                Trophies =
                [
                    new() { Id = "CUP", Name = "Test Cup", Category = "Golf", ReferenceImage = "/catalogue/test.png", Winners =
                    [new() { Id = "winner", Year = 2020, Name = "A. SMITH", ReviewState = ReviewStates.Confirmed, Description = "Private note" },
                     new() { Id = "unconfirmed", Year = 2021, Name = "Pending Person", ReviewState = ReviewStates.NeedsReview }] },
                    new() { Id = "JUNIOR", Name = "Junior Cup", Category = "Golf", Division = TrophyDivisions.Junior,
                        Winners = [new() { Id = "child", Year = 2020, Name = "Junior Winner", ReviewState = ReviewStates.Confirmed }] }
                ]
            };
            await File.WriteAllTextAsync(Path.Combine(root, "catalogue-state.json"), JsonSerializer.Serialize(state, options));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_PATH"] = root, ["SKIP_SEED_CATALOGUE"] = "true" }).Build();
            var environment = new TestEnvironment(root);
            var context = new ClubContextAccessor();
            var accounts = new AccountStore(environment, configuration, new PasswordHasher<AccountRecord>());
            await accounts.InitializeAsync();
            var catalogue = new CatalogueStore(environment, configuration, context);
            return new() { Root = root, Context = context, Catalogue = catalogue, Accounts = accounts, Store = new(environment, configuration, catalogue, accounts, context) };
        }
        public void Dispose()
        {
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "trophy-publication-tests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(Root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(Root, true);
        }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Trophy.Catalogue.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
