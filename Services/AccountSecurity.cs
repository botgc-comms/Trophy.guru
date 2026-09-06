using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class AccountSecurity
{
    public const string AuthenticationScheme = "TrophyArchiveAccount";
    public const string SecurityVersionClaim = "trophy:security-version";
    public static bool IsOwner(AccountRecord? account) => account?.Role == AccountRoles.Owner;
    public static bool IsTrustedLegacyAccount(AccountRecord? account) => IsOwner(account) && account!.HasUnlimitedTrophyCredits && string.Equals(account.ClubId, "legacy", StringComparison.OrdinalIgnoreCase);
    public static bool IsEmailVerified(AccountRecord? account) => account is not null && (account.EmailVerifiedAt.HasValue || IsTrustedLegacyAccount(account));

    public static ClaimsPrincipal CreatePrincipal(AccountRecord account) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, account.Id),
        new Claim(ClaimTypes.Name, account.DisplayName),
        new Claim(ClaimTypes.Email, account.Email),
        new Claim(ClaimTypes.Role, account.Role),
        new Claim(SecurityVersionClaim, account.SecurityVersion.ToString(CultureInfo.InvariantCulture))
    ], AuthenticationScheme));

    public static Task SignInAsync(HttpContext context, AccountRecord account) => context.SignInAsync(AuthenticationScheme,
        CreatePrincipal(account), new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var accountId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
        var account = accountId is null ? null : await accounts.GetAccountAsync(accountId, context.HttpContext.RequestAborted);
        if (account is null || !IsSessionCurrent(context.Principal!, account))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthenticationScheme);
            return;
        }
        // Retain compatible original sessions; stamp them on their next request.
        if (context.Principal!.FindFirstValue(SecurityVersionClaim) is null)
        {
            context.ReplacePrincipal(CreatePrincipal(account));
            context.ShouldRenew = true;
        }
    }

    public static bool IsSessionCurrent(ClaimsPrincipal principal, AccountRecord account)
    {
        var claim = principal.FindFirstValue(SecurityVersionClaim);
        return claim is null ? account.SecurityVersion == 1 :
            int.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version == account.SecurityVersion;
    }

    public static async Task<AccountRecord?> CurrentAccountAsync(HttpContext context, AccountStore accounts, CancellationToken cancellationToken = default)
    {
        if (context.User.Identity?.IsAuthenticated != true) return null;
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = id is null ? null : await accounts.GetAccountAsync(id, cancellationToken);
        return account is not null && IsSessionCurrent(context.User, account) ? account : null;
    }

    public static async Task<AccountRecord?> RequireOwnerAsync(HttpContext context, AccountStore accounts, CancellationToken cancellationToken = default)
    {
        var account = await CurrentAccountAsync(context, accounts, cancellationToken);
        return IsOwner(account) ? account : null;
    }

    public static async Task<bool> IssueVerificationAsync(AccountRecord account, AccountStore accounts, TransactionalEmail email, CancellationToken cancellationToken = default)
    {
        if (IsEmailVerified(account)) return true;
        if (!email.IsAvailable) return false;
        var issued = await accounts.IssueActionTokenAsync(account.Id, AccountStore.VerifyEmailPurpose, cancellationToken);
        if (issued is null) return true;
        if (await email.SendVerificationAsync(issued.Email, issued.Token, cancellationToken)) return true;
        await accounts.RevokeActionTokenAsync(issued.Id, CancellationToken.None);
        return false;
    }
}