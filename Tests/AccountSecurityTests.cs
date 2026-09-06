using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class AccountSecurityTests
{
    [Fact]
    public async Task ExistingLegacyAccountRetainsIdentityCredentialsAndFiles()
    {
        using var fixture = new Fixture(legacy: true);
        var original = new AccountRecord { Id = "original-account", DisplayName = "Existing archive owner", Email = "archive@botgc.test", NormalizedEmail = "ARCHIVE@BOTGC.TEST", ClubId = "legacy", HasUnlimitedTrophyCredits = true, PlanCode = "unlimited", TrophyCreditBalance = 17 };
        original.PasswordHash = fixture.Hasher.HashPassword(original, "ExistingPassword123");
        var created = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "identity.json"), JsonSerializer.Serialize(new
        {
            accounts = new[] { new { original.Id, original.DisplayName, original.Email, original.NormalizedEmail, original.PasswordHash, original.ClubId, original.HasUnlimitedTrophyCredits, original.PlanCode, original.TrophyCreditBalance, CreatedAt = created } },
            clubs = new[] { new { Id = "legacy", Name = "Existing club", Sport = "Golf", Country = "United Kingdom" } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var catalogueBytes = "{\"real-work-fixture\":\"do not change\"}"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "catalogue-state.json"), catalogueBytes);
        var store = await fixture.OpenAsync();
        var initial = await store.GetAccountAsync(original.Id);
        Assert.NotNull(initial);
        Assert.Equal(original.Id, initial.Id);
        Assert.Equal(original.ClubId, initial.ClubId);
        Assert.Equal(original.Email, initial.Email);
        Assert.Equal(original.DisplayName, initial.DisplayName);
        Assert.Equal(original.PasswordHash, initial.PasswordHash);
        Assert.Equal(created, initial.CreatedAt);
        Assert.Equal(17, initial.TrophyCreditBalance);
        Assert.Equal(AccountRoles.Owner, initial.Role);
        Assert.True(initial.HasUnlimitedTrophyCredits);
        Assert.True(AccountSecurity.IsEmailVerified(initial));
        Assert.NotNull(await store.AuthenticateAsync(new LoginInput(original.Email, "ExistingPassword123")));
        var opened = await store.OpenLegacyArchiveAsync("DifferentOriginalLogin456");
        Assert.Equal(original.PasswordHash, opened.PasswordHash);
        Assert.Equal(original.Email, opened.Email);
        Assert.Null(await store.IssueActionTokenAsync(original.Id, AccountStore.PasswordResetPurpose));
        Assert.Null(await store.IssueActionTokenAsync(original.Id, AccountStore.VerifyEmailPurpose));
        Assert.Equal(catalogueBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "catalogue-state.json")));
        var reopened = await fixture.OpenAsync();
        Assert.NotNull(await reopened.AuthenticateAsync(new LoginInput(original.Email, "ExistingPassword123")));
    }

    [Fact]
    public async Task PrivateStoragePreservesHashesButPublicSerializationOmitsThem()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var account = await fixture.CreateAsync(store, "owner@example.test");
        Assert.NotEmpty(account.PasswordHash);
        var apiJson = JsonSerializer.Serialize(account, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("passwordHash", apiJson);
        Assert.DoesNotContain("securityVersion", apiJson);
        Assert.DoesNotContain(account.PasswordHash, apiJson);
        var reopened = await fixture.OpenAsync();
        var authenticated = await reopened.AuthenticateAsync(new LoginInput(account.Email, Fixture.Password));
        Assert.NotNull(authenticated);
        Assert.Equal(account.PasswordHash, authenticated.PasswordHash);
    }

    [Fact]
    public async Task VerificationAndResetTokensAreHashedPurposeBoundAndSingleUse()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var account = await fixture.CreateAsync(store, "owner@example.test");
        var verification = await store.IssueActionTokenAsync(account.Id, AccountStore.VerifyEmailPurpose);
        var reset = await store.IssueActionTokenAsync(account.Id, AccountStore.PasswordResetPurpose);
        Assert.NotNull(verification); Assert.NotNull(reset);
        var identity = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "identity.json"));
        Assert.DoesNotContain(verification.Token, identity); Assert.DoesNotContain(reset.Token, identity);
        Assert.False(await store.VerifyEmailAsync(reset.Token));
        Assert.False(await store.ResetPasswordAsync(verification.Token, "ReplacementPassword123"));
        Assert.True(await store.VerifyEmailAsync(verification.Token));
        Assert.False(await store.VerifyEmailAsync(verification.Token));
        Assert.True(AccountSecurity.IsEmailVerified(await store.GetAccountAsync(account.Id)));
        Assert.False(await store.VerifyEmailAsync(new string('A', 43)));
    }

    [Fact]
    public async Task ConcurrentPasswordResetConsumesOnceAndRevokesEveryOldSessionAndToken()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var account = await fixture.CreateAsync(store, "owner@example.test");
        var principal = AccountSecurity.CreatePrincipal(account);
        var oldPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id)], AccountSecurity.AuthenticationScheme));
        Assert.True(AccountSecurity.IsSessionCurrent(oldPrincipal, account));
        var verification = await store.IssueActionTokenAsync(account.Id, AccountStore.VerifyEmailPurpose);
        var reset = await store.IssueActionTokenAsync(account.Id, AccountStore.PasswordResetPurpose);
        Assert.NotNull(reset); Assert.NotNull(verification);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.ResetPasswordAsync(reset.Token, "ReplacementPassword123")));
        Assert.Single(results, result => result);
        var updated = await store.GetAccountAsync(account.Id); Assert.NotNull(updated);
        Assert.False(AccountSecurity.IsSessionCurrent(principal, updated));
        Assert.False(AccountSecurity.IsSessionCurrent(oldPrincipal, updated));
        Assert.False(await store.VerifyEmailAsync(verification.Token));
        Assert.Null(await store.AuthenticateAsync(new LoginInput(account.Email, Fixture.Password)));
        Assert.NotNull(await store.AuthenticateAsync(new LoginInput(account.Email, "ReplacementPassword123")));
        var reopened = await fixture.OpenAsync();
        Assert.False(AccountSecurity.IsSessionCurrent(principal, (await reopened.GetAccountAsync(account.Id))!));
    }

    [Fact]
    public async Task ExpiredTokensAndRevokedSessionsCannotBeReused()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var account = await fixture.CreateAsync(store, "owner@example.test");
        var verification = await store.IssueActionTokenAsync(account.Id, AccountStore.VerifyEmailPurpose);
        Assert.NotNull(verification);
        var path = Path.Combine(fixture.Root, "identity.json");
        var identity = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        identity["accountActionTokens"]![0]!["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        await File.WriteAllTextAsync(path, identity.ToJsonString());
        store = await fixture.OpenAsync();
        Assert.False(await store.VerifyEmailAsync(verification.Token));
        var reset = await store.IssueActionTokenAsync(account.Id, AccountStore.PasswordResetPurpose); Assert.NotNull(reset);
        await store.RevokeSessionsAsync(account.Id);
        Assert.False(await store.ResetPasswordAsync(reset.Token, "ReplacementPassword123"));
    }

    [Fact]
    public async Task InvitationIsBoundToRecipientSingleUseAndCannotGrantOwnership()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var owner = await fixture.CreateOwnerAsync(store);
        var editor = await fixture.CreateAsync(store, "editor@example.test");
        var stranger = await fixture.CreateAsync(store, "stranger@example.test");
        var invite = await store.CreateInvitationAsync(owner.Id, editor.Email);
        var error = await Assert.ThrowsAsync<AccountStoreException>(() => store.AcceptInvitationAsync(stranger.Id, invite.Token));
        Assert.Equal("invitation_account_mismatch", error.Code);
        var accepted = await store.AcceptInvitationAsync(editor.Id, invite.Token);
        Assert.Equal(owner.ClubId, accepted.ClubId);
        Assert.Equal(AccountRoles.Editor, accepted.Role);
        Assert.True(AccountSecurity.IsEmailVerified(accepted));
        Assert.False(AccountSecurity.IsOwner(accepted));
        await Assert.ThrowsAsync<AccountStoreException>(() => store.AcceptInvitationAsync(editor.Id, invite.Token));
        await Assert.ThrowsAsync<AccountStoreException>(() => store.CreateInvitationAsync(editor.Id, "other@example.test"));
        await Assert.ThrowsAsync<AccountStoreException>(() => store.UpsertClubAsync(editor.Id, new ClubInput("Changed club", "Golf", "UK", null)));
        await Assert.ThrowsAsync<AccountStoreException>(() => store.RemoveEditorAsync(editor.Id, owner.Id));
        await Assert.ThrowsAsync<AccountStoreException>(() => store.RemoveEditorAsync(owner.Id, owner.Id));
        var editorPrincipal = AccountSecurity.CreatePrincipal(accepted);
        await store.RemoveEditorAsync(owner.Id, editor.Id);
        var removed = await store.GetAccountAsync(editor.Id); Assert.NotNull(removed);
        Assert.Null(removed.ClubId);
        Assert.False(AccountSecurity.IsSessionCurrent(editorPrincipal, removed));
        Assert.Equal(owner.ClubId, (await store.GetAccountAsync(owner.Id))!.ClubId);
    }

    [Fact]
    public async Task InvitationNeverReassignsAnAccountThatCreatedItsOwnClub()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var owner = await fixture.CreateOwnerAsync(store);
        var recipient = await fixture.CreateAsync(store, "recipient@example.test");
        var invite = await store.CreateInvitationAsync(owner.Id, recipient.Email);
        var existingClub = await store.UpsertClubAsync(recipient.Id, new ClubInput("Their existing archive", "Golf", "United Kingdom", null));
        var exception = await Assert.ThrowsAsync<AccountStoreException>(() => store.AcceptInvitationAsync(recipient.Id, invite.Token));
        Assert.Equal("account_has_club", exception.Code);
        Assert.Equal(existingClub.Id, (await store.GetAccountAsync(recipient.Id))!.ClubId);
        Assert.Equal("Their existing archive", (await store.GetClubAsync(existingClub.Id))!.Name);
    }

    [Fact]
    public async Task RevokedAndExpiredInvitationsCannotBeAccepted()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var owner = await fixture.CreateOwnerAsync(store);
        var recipient = await fixture.CreateAsync(store, "recipient@example.test");
        var first = await store.CreateInvitationAsync(owner.Id, recipient.Email);
        await store.RevokeInvitationAsync(owner.Id, first.Id);
        await Assert.ThrowsAsync<AccountStoreException>(() => store.AcceptInvitationAsync(recipient.Id, first.Token));
        var next = await store.CreateInvitationAsync(owner.Id, recipient.Email);
        var path = Path.Combine(fixture.Root, "identity.json");
        var identity = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        foreach (var invitation in identity["clubInvitations"]!.AsArray()) invitation!["expiresAt"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        await File.WriteAllTextAsync(path, identity.ToJsonString());
        store = await fixture.OpenAsync();
        await Assert.ThrowsAsync<AccountStoreException>(() => store.AcceptInvitationAsync(recipient.Id, next.Token));
    }

    [Fact]
    public async Task ProductionEmailFailsClosedAndDevelopmentPickupRequiresExplicitConfiguration()
    {
        using var fixture = new Fixture();
        var disabled = new TransactionalEmail(fixture.Configuration, fixture.Environment, NullLogger<TransactionalEmail>.Instance);
        Assert.False(disabled.IsAvailable);
        var pickup = Path.Combine(fixture.Root, "mail");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EMAIL_TRANSPORT"] = "development", ["EMAIL_DEVELOPMENT_DIRECTORY"] = pickup,
            ["EMAIL_FROM"] = "noreply@example.test", ["PUBLIC_SITE_URL"] = "http://127.0.0.1:5199"
        }).Build();
        var development = new TransactionalEmail(config, fixture.Environment, NullLogger<TransactionalEmail>.Instance);
        Assert.True(development.IsAvailable);
        Assert.True(await development.SendVerificationAsync("owner@example.test", new string('A', 43)));
        Assert.Single(Directory.GetFiles(pickup, "*.eml"));
        fixture.Environment.EnvironmentName = "Production";
        var production = new TransactionalEmail(config, fixture.Environment, NullLogger<TransactionalEmail>.Instance);
        Assert.False(production.IsAvailable);
        Assert.False(await production.SendVerificationAsync("owner@example.test", new string('B', 43)));
        Assert.Single(Directory.GetFiles(pickup, "*.eml"));
    }

    [Fact]
    public async Task FailedPersistenceRollsBackPasswordAndSessionChangesInMemory()
    {
        using var fixture = new Fixture();
        var store = await fixture.OpenAsync();
        var account = await fixture.CreateAsync(store, "owner@example.test");
        var principal = AccountSecurity.CreatePrincipal(account);
        var path = Path.Combine(fixture.Root, "identity.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        var failure = await Record.ExceptionAsync(() => store.ChangePasswordAsync(account.Id, Fixture.Password, "ReplacementPassword123"));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.NotNull(await store.AuthenticateAsync(new LoginInput(account.Email, Fixture.Password)));
        Assert.True(AccountSecurity.IsSessionCurrent(principal, (await store.GetAccountAsync(account.Id))!));
    }
    private sealed class Fixture : IDisposable
    {
        public const string Password = "OriginalPassword123";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"trophy-account-security-{Guid.NewGuid():N}");
        public TestEnvironment Environment { get; }
        public IConfiguration Configuration { get; }
        public PasswordHasher<AccountRecord> Hasher { get; } = new();
        public Fixture(bool legacy = false)
        {
            Directory.CreateDirectory(Root);
            Environment = new TestEnvironment { ContentRootPath = Root, WebRootPath = Path.Combine(Root, "wwwroot") };
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_PATH"] = Root, ["APP_PASSWORD"] = legacy ? "ConfiguredLegacyPassword123" : null }).Build();
        }
        public async Task<AccountStore> OpenAsync()
        {
            var store = new AccountStore(Environment, Configuration, Hasher); await store.InitializeAsync(); return store;
        }
        public Task<AccountRecord> CreateAsync(AccountStore store, string email) => store.CreateAccountAsync(new SignupInput("Archive owner", email, Password));
        public async Task<AccountRecord> CreateOwnerAsync(AccountStore store)
        {
            var owner = await CreateAsync(store, "owner@example.test");
            var token = await store.IssueActionTokenAsync(owner.Id, AccountStore.VerifyEmailPurpose); Assert.NotNull(token);
            Assert.True(await store.VerifyEmailAsync(token.Token));
            await store.UpsertClubAsync(owner.Id, new ClubInput("Test club", "Golf", "United Kingdom", null));
            return (await store.GetAccountAsync(owner.Id))!;
        }
        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("trophy-account-security-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test directory.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Trophy.Catalogue.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}