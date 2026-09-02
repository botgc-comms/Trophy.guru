namespace Trophy.Catalogue.Services;

public static class AppDataPath
{
    public static string Resolve(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration["DATA_PATH"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data-store")
            : Path.GetFullPath(configured);
    }

    public static string ClubRoot(string dataRoot, string clubId) =>
        clubId.Equals("legacy", StringComparison.OrdinalIgnoreCase)
            ? dataRoot
            : Path.Combine(dataRoot, "clubs", SafeSegment(clubId));

    public static string SafeSegment(string value) =>
        string.Concat(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
}
