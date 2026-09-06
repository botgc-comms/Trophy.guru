using System.Net;
using System.Net.Mail;

namespace Trophy.Catalogue.Services;

/// <summary>Explicitly configured transactional mail. Never logs message bodies, credentials or action links.</summary>
public sealed class TransactionalEmail(IConfiguration configuration, IWebHostEnvironment environment, ILogger<TransactionalEmail> logger)
{
    public bool IsAvailable => TryGetSettings(out _);
    public string? PublicSiteOrigin => TryGetOrigin(out var origin) ? origin : null;

    public Task<bool> SendVerificationAsync(string email, string token, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Verify your Trophy Archive email", $"Confirm your email address to publish your honours board, purchase trophy credits and invite editors.\n\n{ActionLink("verify", token)}\n\nThis link expires in 24 hours and can be used once. If you did not create this account, you can ignore this message.", cancellationToken);

    public Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Reset your Trophy Archive password", $"Use this link to choose a new password:\n\n{ActionLink("reset", token)}\n\nThis link expires in 30 minutes and can be used once. Resetting your password signs out all existing sessions. If you did not request this, you can ignore this message.", cancellationToken);

    public Task<bool> SendInvitationAsync(string email, string token, string clubName, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Invitation to edit a Trophy Archive", $"You have been invited to help edit the archive for {clubName}. Editors can manage trophies, evidence and winner records. Publication, payments and access settings remain with the club owner.\n\n{ActionLink("invite", token)}\n\nSign in or create an account using this email address, then accept the invitation. Each account can belong to one club. This link expires in 7 days and can be used once. If you were not expecting this, you can ignore this message.", cancellationToken);

    private string ActionLink(string action, string token) => $"{PublicSiteOrigin}/account-security.html#{action}={Uri.EscapeDataString(token)}";

    private async Task<bool> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        if (!TryGetSettings(out var settings)) return false;
        try
        {
            using var message = new MailMessage(new MailAddress(settings.From), new MailAddress(recipient))
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            using var client = new SmtpClient();
            client.UseDefaultCredentials = false;
            if (settings.DevelopmentDirectory is not null)
            {
                Directory.CreateDirectory(settings.DevelopmentDirectory);
                client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                client.PickupDirectoryLocation = settings.DevelopmentDirectory;
            }
            else
            {
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.Host = settings.Host!;
                client.Port = settings.Port;
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(settings.Username, settings.Password);
            }
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is SmtpException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // SMTP exception text can contain a recipient or provider diagnostics.
            logger.LogWarning("Transactional email delivery failed ({FailureCategory}).", exception.GetType().Name);
            return false;
        }
    }

    private bool TryGetSettings(out EmailSettings settings)
    {
        settings = default!;
        if (!TryGetOrigin(out _) || !ValidAddress(configuration["EMAIL_FROM"])) return false;
        var transport = configuration["EMAIL_TRANSPORT"]?.Trim().ToLowerInvariant();
        if (transport == "development")
        {
            var directory = configuration["EMAIL_DEVELOPMENT_DIRECTORY"];
            if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return false;
            var fullDirectory = Path.GetFullPath(directory);
            var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((fullDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(webRoot, StringComparison.OrdinalIgnoreCase)) return false;
            settings = new EmailSettings(configuration["EMAIL_FROM"]!, null, 0, null, null, fullDirectory);
            return true;
        }
        if (transport != "smtp" || string.IsNullOrWhiteSpace(configuration["SMTP_HOST"]) ||
            string.IsNullOrWhiteSpace(configuration["SMTP_USERNAME"]) || string.IsNullOrWhiteSpace(configuration["SMTP_PASSWORD"])) return false;
        var portText = configuration["SMTP_PORT"] ?? "587";
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) return false;
        settings = new EmailSettings(configuration["EMAIL_FROM"]!, configuration["SMTP_HOST"]!, port, configuration["SMTP_USERNAME"]!, configuration["SMTP_PASSWORD"]!, null);
        return true;
    }

    private bool TryGetOrigin(out string origin)
    {
        origin = string.Empty;
        var candidate = configuration["PUBLIC_SITE_URL"] ?? configuration["RENDER_EXTERNAL_URL"];
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/") return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return false;
        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    private static bool ValidAddress(string? value)
    {
        try { return !string.IsNullOrWhiteSpace(value) && new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }

    private sealed record EmailSettings(string From, string? Host, int Port, string? Username, string? Password, string? DevelopmentDirectory);
}