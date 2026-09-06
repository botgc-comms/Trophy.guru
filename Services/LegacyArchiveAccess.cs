namespace Trophy.Catalogue.Services;

// Compatibility navigation only. Authentication always verifies the owner's current account password.
public sealed class LegacyArchiveAccess(IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly string cataloguePath = Path.Combine(AppDataPath.Resolve(environment, configuration), "catalogue-state.json");
    public bool IsAvailable => File.Exists(cataloguePath);
    public bool PasswordRequired => true;
}
