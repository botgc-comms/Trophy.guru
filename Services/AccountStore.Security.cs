using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed partial class AccountStore
{
    public const string VerifyEmailPurpose = "verify-email";
    public const string PasswordResetPurpose = "reset-password";

    // API serialization always omits password hashes and security stamps. Only the
    // private identity store opts these fields back in, including when cloning.
    private static JsonSerializerOptions CreateStorageJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(AccountRecord)) return;
            foreach (var name in new[] { nameof(AccountRecord.PasswordHash), nameof(AccountRecord.SecurityVersion) })
            {
                var member = typeof(AccountRecord).GetProperty(name)!;
                var property = typeInfo.Properties.First(item => item.Name == JsonNamingPolicy.CamelCase.ConvertName(name));
                property.Get = member.GetValue;
                property.Set = member.SetValue;
                property.ShouldSerialize = (_, _) => true;
            }
        });
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = resolver
        };
    }

    public async Task<AccountRecord?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = NormalizeEmail(email); }
        catch (AccountStoreException) { return null; }
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.NormalizedEmail == normalized);
            return account is null ? null : Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task<IssuedAccountToken?> IssueActionTokenAsync(string accountId, string purpose, CancellationToken cancellationToken = default)
    {
        if (purpose is not (VerifyEmailPurpose or PasswordResetPurpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (account is null || (purpose == VerifyEmailPurpose && AccountSecurity.IsEmailVerified(account))) return null;
            // The original archive's .test address has no mailbox. Its existing
            // password and original-archive login remain the recovery mechanism.
            if (AccountSecurity.IsTrustedLegacyAccount(account)) return null;
            var now = DateTimeOffset.UtcNow;
            if (state.AccountActionTokens.Any(item => item.AccountId == accountId && item.Purpose == purpose && item.CreatedAt > now.AddMinutes(-1)))
                return null;
            var raw = CreateToken();
            var action = new AccountActionToken
            {
                Id = Guid.NewGuid().ToString("N"), AccountId = accountId, Purpose = purpose,
                TokenHash = HashToken(raw), SecurityVersion = account.SecurityVersion, CreatedAt = now,
                ExpiresAt = now.Add(purpose == PasswordResetPurpose ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(24))
            };
            state.AccountActionTokens.RemoveAll(item => item.ExpiresAt <= now || (item.AccountId == accountId && item.Purpose == purpose));
            state.AccountActionTokens.Add(action);
            await SaveUnsafeAsync(cancellationToken);
            return new IssuedAccountToken(action.Id, raw, account.Email, action.ExpiresAt);
        }
        finally { gate.Release(); }
    }

    public async Task RevokeActionTokenAsync(string id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (state.AccountActionTokens.RemoveAll(item => item.Id == id) > 0) await SaveUnsafeAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleToken(token)) return false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var action = FindValidTokenUnsafe(token, VerifyEmailPurpose);
            if (action is null) return false;
            var account = state.Accounts.Single(item => item.Id == action.AccountId);
            account.EmailVerifiedAt = DateTimeOffset.UtcNow;
            state.AccountActionTokens.RemoveAll(item => item.AccountId == account.Id && item.Purpose == VerifyEmailPurpose);
            await SaveUnsafeAsync(cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (!IsPlausibleToken(token)) return false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var action = FindValidTokenUnsafe(token, PasswordResetPurpose);
            if (action is null) return false;
            var account = state.Accounts.Single(item => item.Id == action.AccountId);
            account.PasswordHash = passwordHasher.HashPassword(account, password);
            account.SecurityVersion = checked(account.SecurityVersion + 1);
            account.EmailVerifiedAt ??= DateTimeOffset.UtcNow;
            state.AccountActionTokens.RemoveAll(item => item.AccountId == account.Id);
            await SaveUnsafeAsync(cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task RevokeSessionsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new AccountStoreException("account_missing", "Please sign in again.");
            account.SecurityVersion = checked(account.SecurityVersion + 1);
            state.AccountActionTokens.RemoveAll(item => item.AccountId == accountId);
            await SaveUnsafeAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task ChangePasswordAsync(string accountId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (account is null || string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 128 ||
                passwordHasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
                throw new AccountStoreException("incorrect_credentials", "The current password was not recognised.");
            account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
            account.SecurityVersion = checked(account.SecurityVersion + 1);
            state.AccountActionTokens.RemoveAll(item => item.AccountId == accountId);
            await SaveUnsafeAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<ClubTeamView> GetClubTeamAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var owner = RequireOwnerUnsafe(ownerId);
            var now = DateTimeOffset.UtcNow;
            return new ClubTeamView(
                state.Accounts.Where(item => item.ClubId == owner.ClubId).Select(item => new ClubTeamMember(item.Id, item.DisplayName, item.Email, item.Role)).ToList(),
                state.ClubInvitations.Where(item => item.ClubId == owner.ClubId && item.AcceptedAt is null && item.RevokedAt is null && item.ExpiresAt > now)
                    .Select(item => new ClubInvitationView(item.Id, item.Email, item.Role, item.ExpiresAt)).ToList());
        }
        finally { gate.Release(); }
    }

    public async Task<IssuedClubInvitation> CreateInvitationAsync(string ownerId, string email, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var owner = RequireOwnerUnsafe(ownerId);
            if (!AccountSecurity.IsEmailVerified(owner)) throw new AccountStoreException("email_verification_required", "Verify your email before inviting editors.");
            var existing = state.Accounts.FirstOrDefault(item => item.NormalizedEmail == normalized);
            if (existing?.ClubId is not null)
                throw new AccountStoreException("invitation_unavailable", "That address cannot be invited. Use an account that does not already belong to a club.");
            var now = DateTimeOffset.UtcNow;
            var pending = state.ClubInvitations.Where(item => item.ClubId == owner.ClubId && item.RevokedAt is null && item.AcceptedAt is null && item.ExpiresAt > now).ToList();
            if (pending.Count >= 25) throw new AccountStoreException("invitation_limit", "Revoke an unused invitation before adding another.");
            if (pending.Any(item => item.NormalizedEmail == normalized && item.CreatedAt > now.AddMinutes(-1)))
                throw new AccountStoreException("invitation_rate_limit", "Wait a minute before resending this invitation.");
            foreach (var previous in pending.Where(item => item.NormalizedEmail == normalized)) previous.RevokedAt = now;
            var raw = CreateToken();
            var invitation = new ClubInvitation
            {
                Id = Guid.NewGuid().ToString("N"), ClubId = owner.ClubId!, InvitedByAccountId = owner.Id,
                Email = email.Trim(), NormalizedEmail = normalized, TokenHash = HashToken(raw),
                CreatedAt = now, ExpiresAt = now.AddDays(7), Role = AccountRoles.Editor
            };
            state.ClubInvitations.Add(invitation);
            await SaveUnsafeAsync(cancellationToken);
            var clubName = state.Clubs.Single(item => item.Id == owner.ClubId).Name;
            return new IssuedClubInvitation(invitation.Id, raw, invitation.Email, clubName, invitation.ExpiresAt);
        }
        finally { gate.Release(); }
    }

    public async Task RevokeInvitationAsync(string ownerId, string invitationId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var owner = RequireOwnerUnsafe(ownerId);
            var invitation = state.ClubInvitations.FirstOrDefault(item => item.Id == invitationId && item.ClubId == owner.ClubId);
            if (invitation is null || invitation.AcceptedAt is not null) throw new AccountStoreException("invitation_missing", "This invitation is no longer available.");
            invitation.RevokedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<AccountRecord> AcceptInvitationAsync(string accountId, string token, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var hash = IsPlausibleToken(token) ? HashToken(token) : string.Empty;
            var now = DateTimeOffset.UtcNow;
            var invitation = state.ClubInvitations.FirstOrDefault(item => item.TokenHash == hash && item.RevokedAt is null && item.AcceptedAt is null && item.ExpiresAt > now);
            if (invitation is null || !state.Accounts.Any(item => item.Id == invitation.InvitedByAccountId && item.ClubId == invitation.ClubId && AccountSecurity.IsOwner(item)))
                throw new AccountStoreException("invalid_invitation", "This invitation has expired or is no longer available.");
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (account is null || account.NormalizedEmail != invitation.NormalizedEmail)
                throw new AccountStoreException("invitation_account_mismatch", "Sign in using the email address that received this invitation.");
            // Never reassign an existing archive or promote an invited editor.
            if (account.ClubId is not null)
                throw new AccountStoreException("account_has_club", "This account already belongs to a club. Its archive has not been changed.");
            account.ClubId = invitation.ClubId;
            account.Role = AccountRoles.Editor;
            account.EmailVerifiedAt ??= now;
            account.SecurityVersion = checked(account.SecurityVersion + 1);
            invitation.AcceptedAt = now;
            state.AccountActionTokens.RemoveAll(item => item.AccountId == account.Id);
            await SaveUnsafeAsync(cancellationToken);
            return Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task RemoveEditorAsync(string ownerId, string editorId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var owner = RequireOwnerUnsafe(ownerId);
            var editor = state.Accounts.FirstOrDefault(item => item.Id == editorId && item.ClubId == owner.ClubId && item.Role == AccountRoles.Editor);
            if (editor is null) throw new AccountStoreException("editor_missing", "This editor is no longer in your club.");
            editor.ClubId = null;
            editor.SecurityVersion = checked(editor.SecurityVersion + 1);
            state.AccountActionTokens.RemoveAll(item => item.AccountId == editorId);
            foreach (var invitation in state.ClubInvitations.Where(item => item.ClubId == owner.ClubId && item.NormalizedEmail == editor.NormalizedEmail))
                invitation.RevokedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    private AccountRecord RequireOwnerUnsafe(string accountId)
    {
        var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
        if (!AccountSecurity.IsOwner(account) || account!.ClubId is null)
            throw new AccountStoreException("owner_required", "Only the club owner can manage access.");
        return account;
    }

    private AccountActionToken? FindValidTokenUnsafe(string token, string purpose)
    {
        var hash = HashToken(token);
        var action = state.AccountActionTokens.FirstOrDefault(item => item.TokenHash == hash && item.Purpose == purpose && item.ExpiresAt > DateTimeOffset.UtcNow);
        return action is not null && state.Accounts.Any(item => item.Id == action.AccountId && item.SecurityVersion == action.SecurityVersion) ? action : null;
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool IsPlausibleToken(string? token) => token is { Length: 43 } && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record IssuedAccountToken(string Id, string Token, string Email, DateTimeOffset ExpiresAt);
public sealed record IssuedClubInvitation(string Id, string Token, string Email, string ClubName, DateTimeOffset ExpiresAt);
public sealed record ClubTeamMember(string Id, string DisplayName, string Email, string Role);
public sealed record ClubInvitationView(string Id, string Email, string Role, DateTimeOffset ExpiresAt);
public sealed record ClubTeamView(IReadOnlyList<ClubTeamMember> Members, IReadOnlyList<ClubInvitationView> Invitations);