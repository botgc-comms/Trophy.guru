using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public static class AccountSecurityEndpoints
{
    public static void MapAccountSecurity(this WebApplication app)
    {
        app.MapGet("/api/auth/security", async (HttpContext context, AccountStore accounts, TransactionalEmail email, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            return Results.Ok(new
            {
                account.Email, account.DisplayName, account.ClubId, account.Role,
                emailVerified = AccountSecurity.IsEmailVerified(account),
                trustedLegacy = AccountSecurity.IsTrustedLegacyAccount(account),
                emailDeliveryAvailable = email.IsAvailable
            });
        });

        app.MapPost("/api/auth/forgot-password", async (RecoveryInput input, AccountStore accounts, TransactionalEmail email, CancellationToken cancellationToken) =>
        {
            if (!email.IsAvailable) return MailUnavailable();
            var account = await accounts.FindAccountByEmailAsync(input.Email, cancellationToken);
            var issued = account is null ? null : await accounts.IssueActionTokenAsync(account.Id, AccountStore.PasswordResetPurpose, cancellationToken);
            if (issued is not null && !await email.SendPasswordResetAsync(issued.Email, issued.Token, cancellationToken))
                await accounts.RevokeActionTokenAsync(issued.Id, CancellationToken.None);
            // Same response for missing accounts, .test legacy accounts, throttled requests
            // and delivery errors. The response neither confirms an account nor promises delivery.
            return Results.Accepted(value: new { message = "If this account supports email recovery, check your inbox for a reset link. You can request another in a minute." });
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/reset-password", async (HttpContext context, ResetPasswordInput input, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await accounts.ResetPasswordAsync(input.Token, input.Password, cancellationToken)) return InvalidLink();
                await context.SignOutAsync(AccountSecurity.AuthenticationScheme);
                return Results.Ok(new { message = "Password changed. All existing sessions have been signed out. Sign in with your new password." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/verify-email", async (ActionTokenInput input, AccountStore accounts, CancellationToken cancellationToken) =>
            await accounts.VerifyEmailAsync(input.Token, cancellationToken)
                ? Results.Ok(new { message = "Your email is verified. You can return to your archive." })
                : InvalidLink()).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/resend-verification", async (HttpContext context, AccountStore accounts, TransactionalEmail email, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            if (AccountSecurity.IsEmailVerified(account)) return Results.Ok(new { message = "Your account is ready." });
            if (!email.IsAvailable) return MailUnavailable();
            return await AccountSecurity.IssueVerificationAsync(account, accounts, email, cancellationToken)
                ? Results.Accepted(value: new { message = "Check your inbox for the verification link. Allow a minute before requesting another." })
                : MailUnavailable();
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/change-password", async (HttpContext context, ChangePasswordInput input, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            try
            {
                await accounts.ChangePasswordAsync(account.Id, input.CurrentPassword, input.NewPassword, cancellationToken);
                await context.SignOutAsync(AccountSecurity.AuthenticationScheme);
                return Results.Ok(new { message = "Password changed. Sign in again on each device using the new password." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/logout-all", async (HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            await accounts.RevokeSessionsAsync(account.Id, cancellationToken);
            await context.SignOutAsync(AccountSecurity.AuthenticationScheme);
            return Results.Ok(new { message = "All sessions, including this one, have been signed out." });
        }).RequireRateLimiting("authentication");

        app.MapGet("/api/auth/team", async (HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            try { return Results.Ok(await accounts.GetClubTeamAsync(account.Id, cancellationToken)); }
            catch (AccountStoreException exception) { return AccountError(exception); }
        });

        app.MapPost("/api/auth/invitations", async (HttpContext context, InvitationInput input, AccountStore accounts, TransactionalEmail email, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            if (!AccountSecurity.IsOwner(account)) return Results.Json(new { error = "owner_required", message = "Only the club owner can invite editors." }, statusCode: 403);
            if (!email.IsAvailable) return MailUnavailable();
            try
            {
                var issued = await accounts.CreateInvitationAsync(account.Id, input.Email, cancellationToken);
                if (!await email.SendInvitationAsync(issued.Email, issued.Token, issued.ClubName, cancellationToken))
                {
                    await accounts.RevokeInvitationAsync(account.Id, issued.Id, CancellationToken.None);
                    return MailUnavailable();
                }
                return Results.Ok(new { message = "Invitation sent. The link expires in seven days." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        }).RequireRateLimiting("authentication");

        app.MapPost("/api/auth/accept-invitation", async (HttpContext context, ActionTokenInput input, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            try
            {
                var updated = await accounts.AcceptInvitationAsync(account.Id, input.Token, cancellationToken);
                await AccountSecurity.SignInAsync(context, updated);
                return Results.Ok(new { message = "Invitation accepted. You can now edit the club archive." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        }).RequireRateLimiting("authentication");

        app.MapDelete("/api/auth/invitations/{id}", async (string id, HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            try
            {
                await accounts.RevokeInvitationAsync(account.Id, id, cancellationToken);
                return Results.Ok(new { message = "Invitation revoked." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        });

        app.MapDelete("/api/auth/team/{id}", async (string id, HttpContext context, AccountStore accounts, CancellationToken cancellationToken) =>
        {
            var account = await AccountSecurity.CurrentAccountAsync(context, accounts, cancellationToken);
            if (account is null) return Results.Unauthorized();
            try
            {
                await accounts.RemoveEditorAsync(account.Id, id, cancellationToken);
                return Results.Ok(new { message = "Editor access removed. Their existing sessions have been signed out." });
            }
            catch (AccountStoreException exception) { return AccountError(exception); }
        });
    }

    private static IResult MailUnavailable() => Results.Json(new { error = "email_unavailable", message = "Account email is temporarily unavailable. Please try again later." }, statusCode: 503);
    private static IResult InvalidLink() => Results.BadRequest(new { error = "invalid_link", message = "This link has expired or has already been used. Request a new link." });
    private static IResult AccountError(AccountStoreException exception) => Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.Code is "owner_required" or "email_verification_required" ? 403 : 400);
}

public sealed record RecoveryInput(string Email);
public sealed record ResetPasswordInput(string Token, string Password);
public sealed record ChangePasswordInput(string CurrentPassword, string NewPassword);
public sealed record ActionTokenInput(string Token);
public sealed record InvitationInput(string Email);