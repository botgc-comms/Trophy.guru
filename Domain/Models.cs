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
    public string PlanCode { get; set; } = "free";
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
    public int Version { get; set; } = 9;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TrophyRecord> Trophies { get; set; } = [];
}

public sealed class TrophySeed
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? SecondaryName { get; set; }
    public required string Category { get; set; }
    public string Division { get; set; } = TrophyDivisions.Mixed;
    public string? ReferenceImage { get; set; }
}

public sealed class TrophyRecord
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? SecondaryName { get; set; }
    public required string Category { get; set; }
    public string Division { get; set; } = TrophyDivisions.Mixed;
    public string AwardFormat { get; set; } = AwardFormats.Unknown;
    public bool TeamAwardSuggested { get; set; }
    public string? TeamAwardSuggestionReason { get; set; }
    public string? EngravingInstructions { get; set; }
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
    public string? Description { get; set; }
    public string? ExtractionNotes { get; set; }
    [JsonPropertyName("notes")]
    public string? LegacyNotes { get; set; }
    public MemberMatchRecord? MemberMatch { get; set; }
    public bool KeepMemberUnmatched { get; set; }
    public List<string> RejectedMemberIds { get; set; } = [];
    public List<string> EvidenceImageIds { get; set; } = [];
    public WinnerEvidenceReference? EvidenceReference { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WinnerEvidenceReference
{
    public required string ImageId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1000;
    public int Height { get; set; } = 1000;
}

public sealed class MemberMatchRecord
{
    public required string MemberId { get; set; }
    public required string MemberName { get; set; }
    public string? MembershipNumber { get; set; }
    public int? BirthYear { get; set; }
    public int? JoinYear { get; set; }
    public string Gender { get; set; } = MemberGenders.Unknown;
    public double Confidence { get; set; }
    public string State { get; set; } = MemberMatchStates.Possible;
    public required string Explanation { get; set; }
    public bool ManuallySelected { get; set; }
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
    public bool SuggestsTeamAward { get; set; }
    public string TeamAwardReason { get; set; } = string.Empty;
}

public sealed class AiWinner
{
    public int Year { get; set; }
    public required string Winner { get; set; }
    public double Confidence { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ExtractionNotes { get; set; } = string.Empty;
    public int EvidenceImageNumber { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; } = 1000;
    public int RegionHeight { get; set; } = 1000;
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
    public string? BirthDateFingerprint { get; set; }
    public int? BirthYear { get; set; }
    public int? JoinYear { get; set; }
    public string? MembershipNumber { get; set; }
    public string Gender { get; set; } = MemberGenders.Unknown;
    public bool ManuallyAdded { get; set; }
}

public sealed record TrophySummary(
    string Id,
    string Name,
    string? SecondaryName,
    string Category,
    string Division,
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
    int WithJoinYearCount,
    int WithMembershipNumberCount,
    int WithGenderCount,
    string? SourceName,
    DateTimeOffset? ImportedAt);

public sealed record MemberImportResult(int ImportedCount, int UpdatedCount, int MembershipNumbersAdded, int SkippedCount, string SourceName, DateTimeOffset ImportedAt);
public sealed record WinnerInput(int Year, string Name, string ReviewState, string? Description, string? Notes = null);
public sealed record TimelineInput(int? StartYear, int? EndYear);
public sealed record SignupInput(string DisplayName, string Email, string Password);
public sealed record LoginInput(string Email, string Password);
public sealed record LegacyLoginInput(string? Password);
public sealed record ClubInput(string Name, string Sport, string Country, string? Website);
public sealed record TrophyCreateInput(string Name, string? SecondaryName, string Category, string? Code, string? Division);
public sealed record TrophyDivisionInput(string? Division);
public sealed record TrophyAwardFormatInput(string? AwardFormat);
public sealed record TrophyEngravingInstructionsInput(string? Instructions);
public sealed record MemberMatchSelectionInput(string MemberId);
public sealed record ManualMemberInput(string FullName, string? DateOfBirth, string? DateJoined, string? MembershipNumber, string? Gender);

public static class TrophyDivisions
{
    public const string Mixed = "mixed";
    public const string Gents = "gents";
    public const string Ladies = "ladies";
    public const string Junior = "junior";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Gents or "men" or "mens" or "male" => Gents,
        Ladies or "women" or "womens" or "female" => Ladies,
        Junior or "juniors" or "youth" => Junior,
        _ => Mixed
    };
}

public static class AwardFormats
{
    public const string Unknown = "unknown";
    public const string Individual = "individual";
    public const string Team = "team";

    public static bool IsValid(string? value) => value?.Trim().ToLowerInvariant() is Unknown or Individual or Team;

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Individual => Individual,
        Team => Team,
        _ => Unknown
    };
}

public static class MemberGenders
{
    public const string Unknown = "unknown";
    public const string Male = "male";
    public const string Female = "female";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "m" or "male" or "man" or "men" or "gent" or "gents" => Male,
        "f" or "female" or "woman" or "women" or "lady" or "ladies" => Female,
        _ => Unknown
    };
}

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
