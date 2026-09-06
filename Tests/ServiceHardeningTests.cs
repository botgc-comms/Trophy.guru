using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class ServiceHardeningTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("  +SUM(A1:A2)", "\"'  +SUM(A1:A2)\"")]
    [InlineData("-1+2", "\"'-1+2\"")]
    [InlineData("@name", "\"'@name\"")]
    [InlineData("\tvalue", "\"'\tvalue\"")]
    [InlineData("A. Winner", "\"A. Winner\"")]
    [InlineData("A \"Cup\"", "\"A \"\"Cup\"\"\"")]
    public void ExportsNeutraliseFormulasWithoutChangingOrdinaryText(string input, string expected) => Assert.Equal(expected, SpreadsheetExport.Cell(input));

    [Fact]
    public async Task LegacyLoginUsesCurrentPasswordAndRestartPreservesIdentityBytes()
    {
        using var fixture = new Fixture();
        var accounts = await fixture.OpenAsync();
        var original = await accounts.AuthenticateOriginalArchiveAsync(Fixture.Password); Assert.NotNull(original);
        Assert.True(original.HasUnlimitedTrophyCredits); Assert.Equal("archive@botgc.test", original.Email);
        var before = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json"));
        accounts = await fixture.OpenAsync();
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "identity.json")));
        await accounts.ChangePasswordAsync(original.Id, Fixture.Password, "ReplacementPassword456!");
        var changed = await accounts.GetAccountAsync(original.Id); Assert.NotNull(changed);
        Assert.Null(await accounts.AuthenticateOriginalArchiveAsync(Fixture.Password));
        Assert.NotNull(await accounts.AuthenticateOriginalArchiveAsync("ReplacementPassword456!"));
        accounts = await fixture.OpenAsync();
        var reopened = await accounts.AuthenticateOriginalArchiveAsync("ReplacementPassword456!"); Assert.NotNull(reopened);
        Assert.Equal(original.Id, reopened.Id); Assert.Equal(original.ClubId, reopened.ClubId);
        Assert.Equal(changed.PasswordHash, reopened.PasswordHash);
        Assert.Null(await accounts.AuthenticateOriginalArchiveAsync(Fixture.Password));
        Assert.False(AccountSecurity.IsSessionCurrent(AccountSecurity.CreatePrincipal(original), reopened));
        Assert.Equal("{\"trophies\":[]}", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "catalogue-state.json")));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task OriginalArchiveNeverPermitsEmptyPassword(string environment)
    {
        using var fixture = new Fixture(); fixture.Environment.EnvironmentName = environment;
        var accounts = await fixture.OpenAsync();
        Assert.True(new LegacyArchiveAccess(fixture.Environment, fixture.Configuration).PasswordRequired);
        Assert.Null(await accounts.AuthenticateOriginalArchiveAsync(null));
        Assert.Null(await accounts.AuthenticateOriginalArchiveAsync(""));
        Assert.Null(await accounts.AuthenticateOriginalArchiveAsync("WrongPassword123!"));
    }

    [Fact]
    public async Task LargeBodyOperationsHaveSharedConcurrencyWithoutRequiringVerificationForLogo()
    {
        var options = new RateLimiterOptions(); EndpointSecurity.ConfigureLimits(options);
        using var limiter = options.GlobalLimiter!;
        var context = Context("/api/club/logo", new RequestBodyLimit(6 * 1024 * 1024));
        Assert.False(EndpointSecurity.IsVerifiedOperation(context)); Assert.True(EndpointSecurity.IsResourceOperation(context));
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            for (var i = 0; i < 4; i++) { var lease = await limiter.AcquireAsync(context); Assert.True(lease.IsAcquired); leases.Add(lease); }
            using var rejected = await limiter.AcquireAsync(Context("/api/trophies/x/images", new VerifiedArchiveOperation()));
            Assert.False(rejected.IsAcquired);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
        using var retry = await limiter.AcquireAsync(context); Assert.True(retry.IsAcquired);
    }

    [Fact]
    public async Task ExpensiveReadsShareConcurrencyAndRateLimitsWhileOrdinaryReadsRemainAvailable()
    {
        var options = new RateLimiterOptions(); EndpointSecurity.ConfigureLimits(options);
        using var limiter = options.GlobalLimiter!;
        var context = Context("/api/trophies/fixture", new ResourceArchiveOperation());
        context.Request.Method = "GET";
        Assert.False(EndpointSecurity.IsVerifiedOperation(context));
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            for (var i = 0; i < 4; i++) { var lease = await limiter.AcquireAsync(context); Assert.True(lease.IsAcquired); leases.Add(lease); }
            using var rejected = await limiter.AcquireAsync(Context("/api/trophies/fixture/images", new VerifiedArchiveOperation()));
            Assert.False(rejected.IsAcquired);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
        for (var i = 4; i < 20; i++) { using var lease = await limiter.AcquireAsync(context); Assert.True(lease.IsAcquired); }
        using var exhausted = await limiter.AcquireAsync(context); Assert.False(exhausted.IsAcquired);
        var ordinary = Context("/api/trophies"); ordinary.Request.Method = "GET";
        using var unaffected = await limiter.AcquireAsync(ordinary); Assert.True(unaffected.IsAcquired);
    }

    [Theory]
    [InlineData("/api/auth/login", 16384)]
    [InlineData("/api/trophies", 131072)]
    public async Task ContentLengthAndChunkedRequestsReceiveSmallJsonLimit(string path, long expected)
    {
        var context = Context(path); var feature = new BodyFeature(); context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        context.Response.Body = new MemoryStream();
        Assert.True(await EndpointSecurity.ApplyBodyLimitAsync(context)); Assert.Equal(expected, feature.MaxRequestBodySize);
        context.Request.ContentLength = expected + 1;
        Assert.False(await EndpointSecurity.ApplyBodyLimitAsync(context)); Assert.Equal(413, context.Response.StatusCode);
    }

    private static readonly IServiceProvider HttpServices = new ServiceCollection().AddOptions().BuildServiceProvider();
    private static DefaultHttpContext Context(string path, params object[] metadata)
    {
        var context = new DefaultHttpContext { RequestServices = HttpServices }; context.Request.Path = path; context.Request.Method = "POST";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fixture-owner")], "fixture"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "fixture"));
        return context;
    }
    private sealed class BodyFeature : IHttpMaxRequestBodySizeFeature { public bool IsReadOnly => false; public long? MaxRequestBodySize { get; set; } }
    private sealed class Fixture : IDisposable
    {
        public const string Password = "OriginalPassword123!";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "trophy-hardening-test-" + Guid.NewGuid().ToString("N"));
        public TestEnvironment Environment { get; }
        public IConfiguration Configuration { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, "catalogue-state.json"), "{\"trophies\":[]}");
            Environment = new TestEnvironment { ContentRootPath = Root, WebRootPath = Path.Combine(Root, "wwwroot") };
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_PATH"] = Root, ["APP_PASSWORD"] = Password }).Build();
        }
        public async Task<AccountStore> OpenAsync() { var store = new AccountStore(Environment, Configuration, new PasswordHasher<AccountRecord>()); await store.InitializeAsync(); return store; }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("trophy-hardening-test-")) throw new InvalidOperationException("Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Trophy.Catalogue.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
