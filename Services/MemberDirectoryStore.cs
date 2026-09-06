using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class MemberDirectoryStore(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ClubContextAccessor clubContext)
{
    private readonly SemaphoreSlim importGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TenantDirectory> tenants = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string dataRoot = AppDataPath.Resolve(environment, configuration);
    private readonly byte[] birthDateIdentityKey = LoadOrCreateBirthDateIdentityKey(AppDataPath.Resolve(environment, configuration));

    public async Task<MemberDirectorySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            return new MemberDirectorySummary(
                tenant.State.Members.Count,
                tenant.State.Members.Count(member => member.BirthYear.HasValue),
                tenant.State.Members.Count(member => member.JoinYear.HasValue),
                tenant.State.Members.Count(member => !string.IsNullOrWhiteSpace(member.MembershipNumber)),
                tenant.State.Members.Count(member => MemberGenders.Normalize(member.Gender) != MemberGenders.Unknown),
                tenant.State.SourceName,
                tenant.State.ImportedAt);
        }
        finally { tenant.Gate.Release(); }
    }

    public async Task<IReadOnlyList<MemberRecord>> GetMembersAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try { return tenant.State.Members.Select(Clone).ToList(); }
        finally { tenant.Gate.Release(); }
    }

    public async Task<MemberRecord> AddManualMemberAsync(
        ManualMemberInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateManualFields(input);
        var fullName = Clean(input.FullName ?? string.Empty);
        if (fullName.Length < 2) throw new MemberImportException("Enter the member's full name.");
        if (fullName.Length > 200) throw new MemberImportException("Keep the member's name under 200 characters.");

        var dateOfBirth = input.DateOfBirth?.Trim() ?? string.Empty;
        var parsedBirthDate = ParseDateOrYear(dateOfBirth);
        var birthYear = parsedBirthDate.Year;
        if (dateOfBirth.Length > 0 && !birthYear.HasValue)
            throw new MemberImportException("Enter a valid date of birth or four-digit birth year.");
        if (birthYear > DateTime.UtcNow.Year)
            throw new MemberImportException("The member's birth date cannot be in the future.");

        var dateJoined = input.DateJoined?.Trim() ?? string.Empty;
        var joinYear = ParseYearOrDate(dateJoined);
        if (dateJoined.Length > 0 && !joinYear.HasValue)
            throw new MemberImportException("Enter a valid joining date or four-digit joining year.");
        if (joinYear > DateTime.UtcNow.Year)
            throw new MemberImportException("The member's joining date cannot be in the future.");
        if (birthYear.HasValue && joinYear.HasValue && joinYear.Value < birthYear.Value)
            throw new MemberImportException("The joining date cannot be before the member's birth date.");

        var membershipNumber = NullIfEmpty(input.MembershipNumber ?? string.Empty);
        if (membershipNumber?.Length > 80)
            throw new MemberImportException("Keep the membership number under 80 characters.");

        var firstName = GuessFirstName(fullName);
        var member = new MemberRecord
        {
            FullName = fullName,
            FirstName = firstName,
            Initial = firstName.Length > 0 ? firstName[..1] : string.Empty,
            Surname = GuessSurname(fullName),
            BirthDateFingerprint = FingerprintBirthDate(parsedBirthDate.Date),
            BirthYear = birthYear,
            JoinYear = joinYear,
            MembershipNumber = membershipNumber,
            Gender = MemberGenders.Normalize(input.Gender),
            ManuallyAdded = true
        };

        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            var existing = tenant.State.Members.FirstOrDefault(item => SameMember(item, member));
            if (existing is not null) return Clone(existing);

            if (tenant.State.Members.Count >= MemberImportLimits.Members)
                throw new MemberImportException("The directory already contains 10,000 members. No existing members have been changed.");
            var previousState = tenant.State;
            tenant.State = new MemberDirectoryState { Members = previousState.Members.ToList(), SourceName = previousState.SourceName, ImportedAt = previousState.ImportedAt };
            tenant.State.Members.Add(member);
            tenant.State.Members = tenant.State.Members
                .OrderBy(item => item.Surname)
                .ThenBy(item => item.FirstName)
                .ToList();
            tenant.State.SourceName ??= "Manually maintained directory";
            tenant.State.ImportedAt ??= DateTimeOffset.UtcNow;
            try { await SaveUnsafeAsync(tenant, cancellationToken); }
            catch { tenant.State = previousState; throw; }
            return Clone(member);
        }
        finally { tenant.Gate.Release(); }
    }

    public async Task<MemberImportResult> ImportAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!await importGate.WaitAsync(0, cancellationToken))
            throw new MemberImportException("Another member import is in progress. Try again once it finishes.");
        try { return await ImportCoreAsync(fileName, content, cancellationToken); }
        finally { importGate.Release(); }
    }

    private async Task<MemberImportResult> ImportCoreAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var rows = await MemberImportReader.ReadAsync(fileName, content, cancellationToken);
        if (rows.Count < 2) throw new MemberImportException("The member file does not contain any data rows.");

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var columns = ResolveColumns(headers);
        if (columns.FullName < 0 && columns.Surname < 0)
            throw new MemberImportException("Include either a Full name column, or First name and Surname columns.");

        var members = new List<MemberRecord>();
        var skipped = 0;
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullName = Cell(row, columns.FullName);
            var firstName = Cell(row, columns.FirstName);
            var initial = Cell(row, columns.Initial);
            var surname = Cell(row, columns.Surname);
            if (string.IsNullOrWhiteSpace(fullName))
                fullName = string.Join(' ', new[] { firstName, initial, surname }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(fullName))
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(surname)) surname = GuessSurname(fullName);
            if (string.IsNullOrWhiteSpace(firstName)) firstName = GuessFirstName(fullName);
            if (string.IsNullOrWhiteSpace(initial)) initial = firstName.Length > 0 ? firstName[..1] : string.Empty;
            var parsedBirthDate = ParseDateOrYear(Cell(row, columns.DateOfBirth));
            var birthYear = parsedBirthDate.Year;
            var joinYear = ParseYearOrDate(Cell(row, columns.DateJoined));
            if (birthYear.HasValue && joinYear.HasValue && joinYear.Value < birthYear.Value) joinYear = null;
            ValidateImportedFields(fullName, firstName, initial, surname, Cell(row, columns.MembershipNumber));
            members.Add(new MemberRecord
            {
                FullName = Clean(fullName),
                FirstName = Clean(firstName),
                Initial = Clean(initial).TrimEnd('.'),
                Surname = Clean(surname),
                BirthDateFingerprint = FingerprintBirthDate(parsedBirthDate.Date),
                BirthYear = birthYear,
                JoinYear = joinYear,
                MembershipNumber = NullIfEmpty(Cell(row, columns.MembershipNumber)),
                Gender = MemberGenders.Normalize(Cell(row, columns.Gender))
            });
        }

        members = members
            .GroupBy(member => !string.IsNullOrWhiteSpace(member.MembershipNumber)
                ? $"member-number:{NormalizeIdentity(member.MembershipNumber)}"
                : $"name:{NormalizeIdentity(member.FullName)}|{DateIdentity(member)}|{member.JoinYear}|{member.Gender}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(member => member.Surname)
            .ThenBy(member => member.FirstName)
            .ToList();
        if (members.Count == 0) throw new MemberImportException("No usable member names were found in that file.");

        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            var previousState = tenant.State;
            var previousMembers = previousState.Members;
            var updatedCount = 0;
            var membershipNumbersAdded = 0;
            var matchedPreviousIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = FindPreviousMember(previousMembers, members, member, matchedPreviousIds);
                if (previous is null) continue;
                member.Id = previous.Id;
                matchedPreviousIds.Add(previous.Id);
                if (string.IsNullOrWhiteSpace(previous.MembershipNumber) && !string.IsNullOrWhiteSpace(member.MembershipNumber))
                    membershipNumbersAdded++;
                MergeMissingDetails(member, previous);
                updatedCount++;
            }
            members.AddRange(previousMembers.Where(member => member.ManuallyAdded && !members.Any(imported => SameMember(imported, member))));
            if (members.Count > MemberImportLimits.Members)
                throw new MemberImportException("The import and retained manual entries exceed 10,000 members. The existing directory has not been changed.");
            members = members
                .OrderBy(member => member.Surname)
                .ThenBy(member => member.FirstName)
                .ToList();
            tenant.State = new MemberDirectoryState
            {
                Members = members,
                SourceName = Path.GetFileName(fileName),
                ImportedAt = DateTimeOffset.UtcNow
            };
            try { await SaveUnsafeAsync(tenant, cancellationToken); }
            catch { tenant.State = previousState; throw; }
            return new MemberImportResult(members.Count, updatedCount, membershipNumbersAdded, skipped, tenant.State.SourceName!, tenant.State.ImportedAt!.Value);
        }
        finally { tenant.Gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            tenant.State = new MemberDirectoryState();
            if (File.Exists(tenant.StatePath)) File.Delete(tenant.StatePath);
        }
        finally { tenant.Gate.Release(); }
    }

    private async Task<TenantDirectory> GetTenantAsync(CancellationToken cancellationToken)
    {
        var clubId = clubContext.RequireClubId();
        var root = AppDataPath.ClubRoot(dataRoot, clubId);
        var tenant = tenants.GetOrAdd(clubId, _ => new TenantDirectory(Path.Combine(root, "member-directory.json")));
        if (tenant.Initialized) return tenant;
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            if (tenant.Initialized) return tenant;
            Directory.CreateDirectory(Path.GetDirectoryName(tenant.StatePath)!);
            if (File.Exists(tenant.StatePath))
            {
                await using var stream = File.OpenRead(tenant.StatePath);
                tenant.State = await JsonSerializer.DeserializeAsync<MemberDirectoryState>(stream, jsonOptions, cancellationToken) ?? new();
            }
            tenant.Initialized = true;
            return tenant;
        }
        finally { tenant.Gate.Release(); }
    }

    private async Task SaveUnsafeAsync(TenantDirectory tenant, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{tenant.StatePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(stream, tenant.State, jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, tenant.StatePath, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateManualFields(ManualMemberInput input)
    {
        if ((input.FullName?.Length ?? 0) > 200 || (input.MembershipNumber?.Length ?? 0) > 80 ||
            (input.DateOfBirth?.Length ?? 0) > 64 || (input.DateJoined?.Length ?? 0) > 64 || (input.Gender?.Length ?? 0) > 32)
            throw new MemberImportException("Keep names under 201 characters, membership numbers under 81 characters, and dates and gender concise.");
    }

    private static void ValidateImportedFields(string fullName, string firstName, string initial, string surname, string membershipNumber)
    {
        if (new[] { fullName, firstName, initial, surname }.Any(value => value.Length > 200))
            throw new MemberImportException("Keep each imported member name under 201 characters.");
        if (membershipNumber.Length > 80)
            throw new MemberImportException("Keep imported membership numbers under 81 characters.");
    }

    private static MemberColumns ResolveColumns(IReadOnlyList<string> headers) => new(
        Find(headers, "fullname", "membername", "displayname", "name"),
        Find(headers, "firstname", "givenname", "forename"),
        Find(headers, "initial", "initials", "middleinitial"),
        Find(headers, "surname", "lastname", "familyname"),
        Find(headers, "dateofbirth", "dob", "birthdate", "birthyear", "yearofbirth"),
        Find(headers, "datejoined", "joindate", "joineddate", "membershipstartdate", "startdate", "joined", "yearjoined"),
        Find(headers, "membershipnumber", "membernumber", "membershipno", "memberno", "membershipid", "memberid"),
        Find(headers, "gender", "sex", "membergender"));

    private static int Find(IReadOnlyList<string> headers, params string[] names)
    {
        for (var index = 0; index < headers.Count; index++)
            if (names.Contains(headers[index], StringComparer.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private static string NormalizeHeader(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Cell(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
    private static string Clean(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string GuessSurname(string fullName) => Clean(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
    private static string GuessFirstName(string fullName) => Clean(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : Clean(value);
    private static MemberRecord? FindPreviousMember(
        IReadOnlyList<MemberRecord> previousMembers,
        IReadOnlyList<MemberRecord> importedMembers,
        MemberRecord imported,
        IReadOnlySet<string> matchedPreviousIds)
    {
        var membershipNumber = NormalizeIdentity(imported.MembershipNumber);
        if (membershipNumber.Length > 0)
        {
            var numberMatches = previousMembers
                .Where(previous => !matchedPreviousIds.Contains(previous.Id)
                    && NormalizeIdentity(previous.MembershipNumber) == membershipNumber)
                .ToList();
            if (numberMatches.Count == 1) return numberMatches[0];
            if (numberMatches.Count > 1) return null;
        }

        var identityMatches = previousMembers
            .Where(previous => !matchedPreviousIds.Contains(previous.Id) && SameNameAndDateOfBirth(previous, imported))
            .ToList();
        if (identityMatches.Count != 1) return null;

        // A name and DOB match is only safe when this specific old record also
        // identifies exactly one row in the new directory. This protects legacy
        // year-only data when two people share a name and birth year.
        var previousMatch = identityMatches[0];
        return importedMembers.Count(candidate => SameNameAndDateOfBirth(previousMatch, candidate)) == 1
            ? previousMatch
            : null;
    }

    private static bool SameNameAndDateOfBirth(MemberRecord left, MemberRecord right)
    {
        if (!left.BirthYear.HasValue || !right.BirthYear.HasValue || left.BirthYear != right.BirthYear) return false;
        if (!string.IsNullOrWhiteSpace(left.BirthDateFingerprint)
            && !string.IsNullOrWhiteSpace(right.BirthDateFingerprint)
            && !left.BirthDateFingerprint.Equals(right.BirthDateFingerprint, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(left.MembershipNumber) && !string.IsNullOrWhiteSpace(right.MembershipNumber)
            && !NormalizeIdentity(left.MembershipNumber).Equals(NormalizeIdentity(right.MembershipNumber), StringComparison.Ordinal)) return false;

        if (NormalizeIdentity(left.FullName) == NormalizeIdentity(right.FullName)) return true;
        if (NormalizeIdentity(left.Surname) != NormalizeIdentity(right.Surname)) return false;

        var leftFirstName = NormalizeIdentity(left.FirstName);
        var rightFirstName = NormalizeIdentity(right.FirstName);
        if (leftFirstName.Length > 0 && leftFirstName == rightFirstName) return true;

        var leftInitial = NormalizeIdentity(left.Initial).FirstOrDefault();
        var rightInitial = NormalizeIdentity(right.Initial).FirstOrDefault();
        return leftInitial != default && leftInitial == rightInitial && (leftFirstName.Length <= 1 || rightFirstName.Length <= 1);
    }

    private static void MergeMissingDetails(MemberRecord imported, MemberRecord previous)
    {
        imported.MembershipNumber ??= previous.MembershipNumber;
        if (string.IsNullOrWhiteSpace(imported.BirthDateFingerprint)
            && (!imported.BirthYear.HasValue || previous.BirthYear == imported.BirthYear))
            imported.BirthDateFingerprint = previous.BirthDateFingerprint;
        imported.BirthYear ??= previous.BirthYear;
        imported.JoinYear ??= previous.JoinYear;
        if (MemberGenders.Normalize(imported.Gender) == MemberGenders.Unknown)
            imported.Gender = MemberGenders.Normalize(previous.Gender);
    }

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string DateIdentity(MemberRecord member) =>
        !string.IsNullOrWhiteSpace(member.BirthDateFingerprint)
            ? member.BirthDateFingerprint
            : member.BirthYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private string? FingerprintBirthDate(DateOnly? date)
    {
        if (!date.HasValue) return null;
        var value = Encoding.UTF8.GetBytes(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return Convert.ToHexString(HMACSHA256.HashData(birthDateIdentityKey, value));
    }

    private static byte[] LoadOrCreateBirthDateIdentityKey(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "member-identity.key");
        if (File.Exists(path)) return ValidateBirthDateIdentityKey(File.ReadAllBytes(path));

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(key);
            return key;
        }
        catch (IOException) when (File.Exists(path))
        {
            return ValidateBirthDateIdentityKey(File.ReadAllBytes(path));
        }
    }

    private static byte[] ValidateBirthDateIdentityKey(byte[] key)
    {
        if (key.Length < 32) throw new InvalidOperationException("The member identity key is invalid.");
        return key;
    }

    private static bool SameMember(MemberRecord left, MemberRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.MembershipNumber) && !string.IsNullOrWhiteSpace(right.MembershipNumber))
            return NormalizeIdentity(left.MembershipNumber) == NormalizeIdentity(right.MembershipNumber);
        return SameNameAndDateOfBirth(left, right)
            || (NormalizeIdentity(left.FullName) == NormalizeIdentity(right.FullName)
                && left.BirthYear == right.BirthYear
                && (!left.JoinYear.HasValue || !right.JoinYear.HasValue || left.JoinYear == right.JoinYear)
                && (MemberGenders.Normalize(left.Gender) == MemberGenders.Unknown
                    || MemberGenders.Normalize(right.Gender) == MemberGenders.Unknown
                    || MemberGenders.Normalize(left.Gender) == MemberGenders.Normalize(right.Gender)));
    }

    private static int? ParseYearOrDate(string value)
        => ParseDateOrYear(value).Year;

    private static ParsedDateValue ParseDateOrYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new(null, null);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) && year is >= 1850 and <= 2200)
            return new(year, null);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 200000)
        {
            try
            {
                var excelDate = DateTime.FromOADate(serial);
                if (excelDate.Year is >= 1850 and <= 2200)
                    return new(excelDate.Year, DateOnly.FromDateTime(excelDate));
            }
            catch (ArgumentException) { }
        }

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyyMMdd",
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
            "M/d/yyyy", "MM/dd/yyyy", "d MMM yyyy", "dd MMM yyyy"
        ];
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exactDate)
            && exactDate.Year is >= 1850 and <= 2200)
            return new(exactDate.Year, DateOnly.FromDateTime(exactDate));
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantDate)
            && invariantDate.Year is >= 1850 and <= 2200)
            return new(invariantDate.Year, DateOnly.FromDateTime(invariantDate));
        return new(null, null);
    }

    private T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, jsonOptions), jsonOptions)!;
    private sealed record ParsedDateValue(int? Year, DateOnly? Date);
    private sealed record MemberColumns(int FullName, int FirstName, int Initial, int Surname, int DateOfBirth, int DateJoined, int MembershipNumber, int Gender);
    private sealed class TenantDirectory(string statePath)
    {
        public string StatePath { get; } = statePath;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public MemberDirectoryState State { get; set; } = new();
        public bool Initialized { get; set; }
    }
}

public sealed class MemberImportException(string message) : Exception(message);
