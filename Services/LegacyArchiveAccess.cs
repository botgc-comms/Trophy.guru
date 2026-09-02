using System.Security.Cryptography;
using System.Text;

namespace Trophy.Catalogue.Services;

public sealed class LegacyArchiveAccess(
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    private readonly string configuredPassword = configuration["APP_PASSWORD"] ?? string.Empty;
    private readonly string cataloguePath = Path.Combine(AppDataPath.Resolve(environment, configuration), "catalogue-state.json");

    public bool IsAvailable => File.Exists(cataloguePath) && (environment.IsDevelopment() || PasswordRequired);
    public bool PasswordRequired => !string.IsNullOrWhiteSpace(configuredPassword);

    public bool PasswordMatches(string? providedPassword)
    {
        if (!File.Exists(cataloguePath)) return false;
        if (environment.IsDevelopment() && !PasswordRequired) return true;
        if (!PasswordRequired || providedPassword is null) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(providedPassword)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configuredPassword)));
    }
}
