using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class AccountStore(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IPasswordHasher<AccountRecord> passwordHasher)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly string dataRoot = AppDataPath.Resolve(environment, configuration);
    private IdentityState state = new();

    private string StatePath => Path.Combine(dataRoot, "identity.json");
    public bool LegacyArchiveExists => File.Exists(Path.Combine(dataRoot, "catalogue-state.json"));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataRoot);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(StatePath))
            {
                await using var stream = File.OpenRead(StatePath);
                state = await JsonSerializer.DeserializeAsync<IdentityState>(stream, jsonOptions, cancellationToken) ?? new();
            }
        }
        finally { gate.Release(); }

        var legacyPassword = configuration["APP_PASSWORD"];
        if (LegacyArchiveExists && !string.IsNullOrWhiteSpace(legacyPassword))
            await OpenLegacyArchiveAsync(legacyPassword, cancellationToken);
    }

    public async Task<AccountRecord> CreateAccountAsync(SignupInput input, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(input.Email);
        var displayName = input.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > 100)
            throw new AccountStoreException("invalid_name", "Enter your name using 2 to 100 characters.");
        ValidatePassword(input.Password);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (state.Accounts.Any(item => item.NormalizedEmail == email))
                throw new AccountStoreException("email_in_use", "An account already exists for that email address.");
            var account = new AccountRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = displayName,
                Email = input.Email.Trim(),
                NormalizedEmail = email
            };
            account.PasswordHash = passwordHasher.HashPassword(account, input.Password);
            state.Accounts.Add(account);
            await SaveUnsafeAsync(cancellationToken);
            return Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task<AccountRecord?> AuthenticateAsync(LoginInput input, CancellationToken cancellationToken = default)
    {
        string email;
        try { email = NormalizeEmail(input.Email); }
        catch (AccountStoreException) { return null; }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.NormalizedEmail == email);
            if (account is null) return null;
            var result = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, input.Password);
            if (result == PasswordVerificationResult.Failed) return null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                account.PasswordHash = passwordHasher.HashPassword(account, input.Password);
                await SaveUnsafeAsync(cancellationToken);
            }
            return Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task<AccountRecord> OpenLegacyArchiveAsync(string credential, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var club = state.Clubs.FirstOrDefault(item => item.Id.Equals("legacy", StringComparison.OrdinalIgnoreCase));
            if (club is null)
            {
                club = new ClubRecord
                {
                    Id = "legacy",
                    Name = configuration["LEGACY_CLUB_NAME"] ?? "Burton-on-Trent Golf Club",
                    Sport = configuration["LEGACY_CLUB_SPORT"] ?? "Golf",
                    Country = configuration["LEGACY_CLUB_COUNTRY"] ?? "United Kingdom"
                };
                state.Clubs.Add(club);
            }

            var configuredEmail = configuration["LEGACY_ARCHIVE_EMAIL"] ?? "archive@botgc.test";
            var normalizedEmail = NormalizeEmail(configuredEmail);
            var account = state.Accounts.FirstOrDefault(item => string.Equals(item.ClubId, "legacy", StringComparison.OrdinalIgnoreCase))
                ?? state.Accounts.FirstOrDefault(item => item.NormalizedEmail == normalizedEmail);
            if (account is null)
            {
                account = new AccountRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DisplayName = configuration["LEGACY_ARCHIVE_DISPLAY_NAME"] ?? "Trophy archive administrator",
                    Email = configuredEmail,
                    NormalizedEmail = normalizedEmail,
                    ClubId = "legacy"
                };
                var passwordMaterial = string.IsNullOrEmpty(credential) ? Guid.NewGuid().ToString("N") : credential;
                account.PasswordHash = passwordHasher.HashPassword(account, passwordMaterial);
                state.Accounts.Add(account);
            }
            else
            {
                account.DisplayName = configuration["LEGACY_ARCHIVE_DISPLAY_NAME"] ?? "Trophy archive administrator";
                account.Email = configuredEmail;
                account.NormalizedEmail = normalizedEmail;
                account.ClubId = "legacy";
                if (!string.IsNullOrEmpty(credential))
                    account.PasswordHash = passwordHasher.HashPassword(account, credential);
            }

            account.HasUnlimitedTrophyCredits = true;
            club.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync(cancellationToken);
            return Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task<AccountRecord?> GetAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            return account is null ? null : Clone(account);
        }
        finally { gate.Release(); }
    }

    public async Task<ClubRecord?> GetClubForAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            var club = account?.ClubId is null ? null : state.Clubs.FirstOrDefault(item => item.Id == account.ClubId);
            return club is null ? null : Clone(club);
        }
        finally { gate.Release(); }
    }

    public async Task<ClubRecord> UpsertClubAsync(string accountId, ClubInput input, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var sport = input.Sport?.Trim() ?? string.Empty;
        var country = input.Country?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 160) throw new AccountStoreException("invalid_club", "Enter a club name using 2 to 160 characters.");
        if (sport.Length is < 2 or > 80) throw new AccountStoreException("invalid_sport", "Enter the club's sport or activity.");
        if (country.Length is < 2 or > 100) throw new AccountStoreException("invalid_country", "Enter the club's country.");
        var website = NormalizeWebsite(input.Website);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new AccountStoreException("account_missing", "Your account could not be found.");
            var club = account.ClubId is null ? null : state.Clubs.FirstOrDefault(item => item.Id == account.ClubId);
            if (club is null)
            {
                club = new ClubRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Sport = sport,
                    Country = country
                };
                state.Clubs.Add(club);
                account.ClubId = club.Id;
            }
            club.Name = name;
            club.Sport = sport;
            club.Country = country;
            club.Website = website;
            club.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync(cancellationToken);
            return Clone(club);
        }
        finally { gate.Release(); }
    }

    public async Task<ClubRecord> SaveClubLogoAsync(
        string accountId,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new AccountStoreException("account_missing", "Your account could not be found.");
            var club = account.ClubId is null ? null : state.Clubs.FirstOrDefault(item => item.Id == account.ClubId);
            if (club is null) throw new AccountStoreException("club_missing", "Save the club details before uploading its logo.");

            var extension = contentType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
            var brandRoot = Path.Combine(AppDataPath.ClubRoot(dataRoot, club.Id), "brand");
            Directory.CreateDirectory(brandRoot);
            var storedName = $"logo{extension}";
            var path = Path.Combine(brandRoot, storedName);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await content.CopyToAsync(output, cancellationToken);
            File.Move(temporaryPath, path, true);
            if (!string.IsNullOrWhiteSpace(club.LogoStoredName) && !club.LogoStoredName.Equals(storedName, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = Path.Combine(brandRoot, Path.GetFileName(club.LogoStoredName));
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
            club.LogoStoredName = storedName;
            club.LogoContentType = contentType;
            club.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync(cancellationToken);
            return Clone(club);
        }
        finally { gate.Release(); }
    }

    public async Task<(string Path, string ContentType)?> GetLogoAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = state.Accounts.FirstOrDefault(item => item.Id == accountId);
            var club = account?.ClubId is null ? null : state.Clubs.FirstOrDefault(item => item.Id == account.ClubId);
            if (club?.LogoStoredName is null || club.LogoContentType is null) return null;
            var path = Path.Combine(AppDataPath.ClubRoot(dataRoot, club.Id), "brand", Path.GetFileName(club.LogoStoredName));
            return File.Exists(path) ? (path, club.LogoContentType) : null;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> GetClubIdsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return state.Clubs.Where(IsClubComplete).Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        finally { gate.Release(); }
    }

    public bool IsClubComplete(ClubRecord? club)
    {
        if (club?.Id.Equals("legacy", StringComparison.OrdinalIgnoreCase) == true && LegacyArchiveExists) return true;
        if (club is null || string.IsNullOrWhiteSpace(club.Name) || string.IsNullOrWhiteSpace(club.Sport) ||
            string.IsNullOrWhiteSpace(club.Country) || string.IsNullOrWhiteSpace(club.LogoStoredName)) return false;
        var path = Path.Combine(AppDataPath.ClubRoot(dataRoot, club.Id), "brand", Path.GetFileName(club.LogoStoredName));
        return File.Exists(path);
    }

    public static string? LogoUrl(ClubRecord? club) => club?.LogoStoredName is null
        ? null
        : $"/api/club/logo?v={club.UpdatedAt.ToUnixTimeSeconds()}";

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        var temporaryPath = $"{StatePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await JsonSerializer.SerializeAsync(stream, state, jsonOptions, cancellationToken);
        File.Move(temporaryPath, StatePath, true);
    }

    private static string NormalizeEmail(string value)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            if (!address.Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) throw new FormatException();
            return address.Address.ToUpperInvariant();
        }
        catch { throw new AccountStoreException("invalid_email", "Enter a valid email address."); }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 10 or > 128)
            throw new AccountStoreException("invalid_password", "Use a password between 10 and 128 characters.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new AccountStoreException("invalid_password", "Include at least one letter and one number in the password.");
    }

    private static string? NormalizeWebsite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AccountStoreException("invalid_website", "Enter a complete website address beginning with https://, or leave it blank.");
        return uri.ToString();
    }

    private T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, jsonOptions), jsonOptions)!;
}

public sealed class AccountStoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
