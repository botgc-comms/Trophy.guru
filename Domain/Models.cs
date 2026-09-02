using System.Text.Json.Serialization;

namespace Trophy.Catalogue.Domain;

public sealed class IdentityState
{
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AccountRecord> Accounts { get; set; } = [];
    public List<ClubRecord> Clubs { get; set; } = [];
}

public sealed class AccountRecord
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? ClubId { get; set; }
    public int TrophyCreditBalance { get; set; } = 1;
    public bool HasUnlimitedTrophyCredits { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClubRecord
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Sport { get; set; }
    public required string Country { get; set; }
    public string? Website { get; set; }
    public string? LogoStoredName { get; set; }
    public string? LogoContentType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CatalogueState
{
    public int Version { get; set; } = 3;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TrophyRecord> Trophies { get; set; } = [];
}

public sealed class TrophySeed
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? SecondaryName { get; set; }
    public required string Category { get; set; }
    public string? ReferenceImage { get; set; }
}

public sealed class TrophyRecord
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? SecondaryName { get; set; }
    public required string Category { get; set; }
    public string? ReferenceImage { get; set; }
    public string IllustrationState { get; set; } = IllustrationStates.None;
    public string? IllustrationMessage { get; set; }
    public int IllustrationGenerationCount { get; set; }
    public DateTimeOffset? IllustrationGeneratedAt { get; set; }
    public string Status { get; set; } = TrophyStatuses.NotStarted;
    public int? TimelineStartYear { get; set; }
    public int? TimelineEndYear { get; set; }
    public DateTimeOffset? LastSavedAt { get; set; }
    public List<WinnerRecord> Winners { get; set; } = [];
    public List<EvidenceImage> TrophyPhotos { get; set; } = [];
    public List<EvidenceImage> Evidence { get; set; } = [];
}

public sealed class WinnerRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Year { get; set; }
    public required string Name { get; set; }
    public double Confidence { get; set; } = 1;
    public string ReviewState { get; set; } = ReviewStates.NeedsReview;
    public string Source { get; set; } = WinnerSources.Manual;
    public string? Notes { get; set; }
    public MemberMatchRecord? MemberMatch { get; set; }
    public List<string> RejectedMemberIds { get; set; } = [];
    public List<string> EvidenceImageIds { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MemberMatchRecord
{
    public required string MemberId { get; set; }
    public required string MemberName { get; set; }
    public string? MembershipNumber { get; set; }
    public int? BirthYear { get; set; }
    public double Confidence { get; set; }
    public string State { get; set; } = MemberMatchStates.Possible;
    public required string Explanation { get; set; }
}

public sealed class EvidenceImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string OriginalName { get; set; }
    [JsonIgnore]
    public string StoredName { get; set; } = string.Empty;
    public required string ContentType { get; set; }
    public string Kind { get; set; } = EvidenceKinds.Photo;
    public string ProcessingState { get; set; } = ProcessingStates.Pending;
    public string? ProcessingMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Url { get; set; } = string.Empty;
}

public sealed class AiExtraction
{
    public List<AiWinner> Entries { get; set; } = [];
    public List<string> Observations { get; set; } = [];
}

public sealed class AiWinner
{
    public int Year { get; set; }
    public required string Winner { get; set; }
    public double Confidence { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class MemberDirectoryState
{
    public List<MemberRecord> Members { get; set; } = [];
    public string? SourceName { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
}

public sealed class MemberRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string FullName { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string Initial { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public string? MembershipNumber { get; set; }
}

public sealed record TrophySummary(
    string Id,
    string Name,
    string? SecondaryName,
    string Category,
    string? ReferenceImage,
    string Status,
    int WinnerCount,
    int EvidenceCount,
    int NeedsReviewCount,
    int MissingYearCount,
    DateTimeOffset? LastSavedAt);

public sealed record MemberDirectorySummary(
    int MemberCount,
    int WithBirthYearCount,
    int WithMembershipNumberCount,
    string? SourceName,
    DateTimeOffset? ImportedAt);

public sealed record MemberImportResult(int ImportedCount, int SkippedCount, string SourceName, DateTimeOffset ImportedAt);
public sealed record WinnerInput(int Year, string Name, string ReviewState, string? Notes);
public sealed record TimelineInput(int? StartYear, int? EndYear);
public sealed record SignupInput(string DisplayName, string Email, string Password);
public sealed record LoginInput(string Email, string Password);
public sealed record LegacyLoginInput(string? Password);
public sealed record ClubInput(string Name, string Sport, string Country, string? Website);
public sealed record TrophyCreateInput(string Name, string? SecondaryName, string Category, string? Code);

public static class TrophyStatuses
{
    public const string NotStarted = "not-started";
    public const string InProgress = "in-progress";
    public const string Complete = "complete";
}

public static class ReviewStates
{
    public const string NeedsReview = "needs-review";
    public const string Confirmed = "confirmed";
}

public static class WinnerSources
{
    public const string Ai = "ai";
    public const string Manual = "manual";
}

public static class EvidenceKinds
{
    public const string Photo = "photo";
    public const string Rubbing = "rubbing";
}

public static class ProcessingStates
{
    public const string Pending = "pending";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public static class IllustrationStates
{
    public const string None = "none";
    public const string Processing = "processing";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public static class MemberMatchStates
{
    public const string Strong = "strong";
    public const string Possible = "possible";
}
