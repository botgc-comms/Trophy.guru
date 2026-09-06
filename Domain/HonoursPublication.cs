namespace Trophy.Catalogue.Domain;

public sealed class HonoursPublication
{
    public int Version { get; set; } = 1;
    public bool IsPublic { get; set; }
    public string? Revision { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public HonoursPublicationOptions Options { get; set; } = new();
    public PublishedHonours? Snapshot { get; set; }
    public Dictionary<string, PublishedAsset> Assets { get; set; } = [];
    public List<PublicationAuditEntry> Audit { get; set; } = [];
}

public sealed class HonoursPublicationOptions
{
    public string NamePolicy { get; set; } = "inscription";
    public bool IncludeDescriptions { get; set; }
    public bool IncludeJuniorTrophies { get; set; }
    public List<string> SelectedWinnerKeys { get; set; } = [];
    public List<string> AllowedEmbedOrigins { get; set; } = [];
}

public sealed record PublishHonoursInput(HonoursPublicationOptions Options, string PreviewFingerprint, bool PublicationApproved);
public sealed record PublicationAuditEntry(DateTimeOffset At, string ActorId, string Action, int WinnerCount);
public sealed record PublishedAsset(string FileName, string ContentType);
public sealed record PublishedHonours(PublishedClub Club, PublishedSummary Summary, List<PublishedTrophy> Trophies);
public sealed record PublishedClub(string Id, string Name, string Sport, string Country, string? Website, string? LogoUrl);
public sealed record PublishedSummary(int Trophies, int Honours, int People, int Years, int? FirstYear, int? LatestYear);
public sealed record PublishedTrophy(string Id, string Name, string? SecondaryName, string Category, string Division, string? ImageUrl, List<PublishedWinner> Winners);
public sealed record PublishedWinner(int Year, string Name, string? Description, string PersonId);
public sealed record PublicationCandidate(string Key, string TrophyName, string Division, int Year, string InscriptionName, string? ApprovedIdentityName, string? Description);
public sealed record PublicationPreview(PublishedHonours Snapshot, string Fingerprint, HonoursPublicationOptions Options);
