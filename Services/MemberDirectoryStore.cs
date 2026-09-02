using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class MemberDirectoryStore(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ClubContextAccessor clubContext)
{
    private readonly ConcurrentDictionary<string, TenantDirectory> tenants = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string dataRoot = AppDataPath.Resolve(environment, configuration);

    public async Task<MemberDirectorySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            return new MemberDirectorySummary(
                tenant.State.Members.Count,
                tenant.State.Members.Count(member => member.BirthYear.HasValue),
                tenant.State.Members.Count(member => !string.IsNullOrWhiteSpace(member.MembershipNumber)),
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

    public async Task<MemberImportResult> ImportAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = extension == ".xlsx" ? ReadXlsx(content) : await ReadDelimitedAsync(content, cancellationToken);
        if (rows.Count < 2) throw new MemberImportException("The member file does not contain any data rows.");

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var columns = ResolveColumns(headers);
        if (columns.FullName < 0 && columns.Surname < 0)
            throw new MemberImportException("Include either a Full name column, or First name and Surname columns.");

        var members = new List<MemberRecord>();
        var skipped = 0;
        foreach (var row in rows.Skip(1))
        {
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
            members.Add(new MemberRecord
            {
                FullName = Clean(fullName),
                FirstName = Clean(firstName),
                Initial = Clean(initial).TrimEnd('.'),
                Surname = Clean(surname),
                BirthYear = ParseBirthYear(Cell(row, columns.DateOfBirth)),
                MembershipNumber = NullIfEmpty(Cell(row, columns.MembershipNumber))
            });
        }

        members = members
            .GroupBy(member => $"{member.MembershipNumber}|{member.FullName}|{member.BirthYear}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(member => member.Surname)
            .ThenBy(member => member.FirstName)
            .ToList();
        if (members.Count == 0) throw new MemberImportException("No usable member names were found in that file.");

        var tenant = await GetTenantAsync(cancellationToken);
        await tenant.Gate.WaitAsync(cancellationToken);
        try
        {
            tenant.State = new MemberDirectoryState
            {
                Members = members,
                SourceName = Path.GetFileName(fileName),
                ImportedAt = DateTimeOffset.UtcNow
            };
            await SaveUnsafeAsync(tenant, cancellationToken);
            return new MemberImportResult(members.Count, skipped, tenant.State.SourceName!, tenant.State.ImportedAt!.Value);
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
        await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await JsonSerializer.SerializeAsync(stream, tenant.State, jsonOptions, cancellationToken);
        File.Move(temporaryPath, tenant.StatePath, true);
    }

    private async Task<List<List<string>>> ReadDelimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, true, 81920, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return ParseDelimited(text, DetectSeparator(text));
    }

    private static char DetectSeparator(string text)
    {
        var firstLine = text.Split(new[] { "\r\n", "\n" }, 2, StringSplitOptions.None)[0];
        var candidates = new[] { ',', '\t', ';' };
        return candidates.OrderByDescending(separator => firstLine.Count(character => character == separator)).First();
    }

    private static List<List<string>> ParseDelimited(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == separator && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
                row = [];
            }
            else field.Append(character);
        }
        row.Add(field.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
        return rows;
    }

    private static List<List<string>> ReadXlsx(Stream content)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var workbook = LoadXml(archive, "xl/workbook.xml");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var relationshipId = workbook.Descendants(main + "sheet").FirstOrDefault()?.Attribute(rel + "id")?.Value
            ?? throw new MemberImportException("The workbook does not contain a worksheet.");
        var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var target = relationships.Descendants(packageRel + "Relationship")
            .FirstOrDefault(node => node.Attribute("Id")?.Value == relationshipId)?.Attribute("Target")?.Value
            ?? throw new MemberImportException("The first worksheet could not be opened.");
        target = target.Replace('\\', '/').TrimStart('/');
        var sheetPath = target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : $"xl/{target}";
        var sheet = LoadXml(archive, sheetPath);

        var rows = new List<List<string>>();
        foreach (var rowElement in sheet.Descendants(main + "row"))
        {
            var values = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements(main + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? string.Empty;
                var columnIndex = ColumnIndex(reference);
                var type = cell.Attribute("t")?.Value;
                string value;
                if (type == "inlineStr") value = string.Concat(cell.Descendants(main + "t").Select(node => node.Value));
                else
                {
                    value = cell.Element(main + "v")?.Value ?? string.Empty;
                    if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                        value = sharedStrings[sharedIndex];
                }
                values[columnIndex] = value;
            }
            if (values.Count == 0) continue;
            var output = Enumerable.Repeat(string.Empty, values.Keys.Max() + 1).ToList();
            foreach (var (index, value) in values) output[index] = value;
            rows.Add(output);
        }
        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(main + "si")
            .Select(item => string.Concat(item.Descendants(main + "t").Select(node => node.Value)))
            .ToList();
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new MemberImportException($"The workbook component '{path}' is missing.");
        if (entry.Length > 50 * 1024 * 1024) throw new MemberImportException("That workbook expands beyond the safe import limit.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static int ColumnIndex(string reference)
    {
        var result = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) result = result * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return Math.Max(0, result - 1);
    }

    private static MemberColumns ResolveColumns(IReadOnlyList<string> headers) => new(
        Find(headers, "fullname", "membername", "displayname", "name"),
        Find(headers, "firstname", "givenname", "forename"),
        Find(headers, "initial", "initials", "middleinitial"),
        Find(headers, "surname", "lastname", "familyname"),
        Find(headers, "dateofbirth", "dob", "birthdate", "birthyear", "yearofbirth"),
        Find(headers, "membershipnumber", "membernumber", "membershipno", "memberno", "membershipid", "memberid"));

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

    private static int? ParseBirthYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) && year is >= 1850 and <= 2200) return year;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 200000)
        {
            try
            {
                var excelDate = DateTime.FromOADate(serial);
                if (excelDate.Year is >= 1850 and <= 2200) return excelDate.Year;
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
            && exactDate.Year is >= 1850 and <= 2200) return exactDate.Year;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantDate)
            && invariantDate.Year is >= 1850 and <= 2200) return invariantDate.Year;
        return null;
    }

    private T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, jsonOptions), jsonOptions)!;
    private sealed record MemberColumns(int FullName, int FirstName, int Initial, int Surname, int DateOfBirth, int MembershipNumber);
    private sealed class TenantDirectory(string statePath)
    {
        public string StatePath { get; } = statePath;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public MemberDirectoryState State { get; set; } = new();
        public bool Initialized { get; set; }
    }
}

public sealed class MemberImportException(string message) : Exception(message);
