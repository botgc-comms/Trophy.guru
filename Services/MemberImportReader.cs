using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Trophy.Catalogue.Services;

// Limits apply before a parsed directory can replace any saved club data.
public static class MemberImportLimits
{
    public const int Members = 10_000;
    public const int Rows = Members + 1;
    public const int Columns = 64;
    public const int Cells = 250_000;
    public const int FieldCharacters = 2_048;
    public const int TableCharacters = 4 * 1024 * 1024;
    public const int UploadBytes = 15 * 1024 * 1024;
    public const int ExpandedBytes = 24 * 1024 * 1024;
    public const int ArchiveEntries = 256;
    public const int XmlCharacters = 16 * 1024 * 1024;
    public const int XmlNodes = 1_300_000;
    public const int XmlDepth = 32;
    public const int SharedStrings = 100_000;
}

internal static class MemberImportReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static async Task<List<List<string>>> ReadAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".tsv" or ".xml" or ".xlsx"))
            throw new MemberImportException("Use a CSV, TSV, XML or XLSX member export.");
        if (Path.GetFileName(fileName).Length > 255)
            throw new MemberImportException("Keep the export filename under 256 characters.");
        if (content.CanSeek && content.Length - content.Position > MemberImportLimits.UploadBytes)
            throw new MemberImportException("Keep the member export below 15 MB.");
        try
        {
            using var buffered = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = await content.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (buffered.Length + count > MemberImportLimits.UploadBytes)
                    throw new MemberImportException("Keep the member export below 15 MB.");
                await buffered.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            buffered.Position = 0;
            return extension switch
            {
                ".xlsx" => ReadXlsx(buffered, cancellationToken),
                ".xml" => ReadXml(buffered, cancellationToken),
                _ => ReadDelimited(buffered, cancellationToken)
            };
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or DecoderFallbackException or FormatException or ArgumentException)
        {
            throw new MemberImportException("The member export is malformed or exceeds the safe import limits. Export a fresh CSV, XML or XLSX file and try again.");
        }
    }

    private static List<List<string>> ReadDelimited(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), true, 81920, leaveOpen: true);
        var text = reader.ReadToEnd();
        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0 ? text : text[..firstLineEnd];
        var separator = new[] { ',', '\t', ';' }.OrderByDescending(candidate => firstLine.Count(character => character == candidate)).First();
        var table = new TableBudget();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { Append('"'); index++; }
                else if (quoted) { quoted = false; closedQuote = true; }
                else if (field.Length == 0 && !closedQuote) quoted = true;
                else throw new MemberImportException("A CSV field contains an incorrectly quoted value.");
            }
            else if (character == separator && !quoted) FinishField();
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                FinishField(); table.AddRow(row); row = [];
            }
            else if (closedQuote)
            {
                if (!char.IsWhiteSpace(character)) throw new MemberImportException("A CSV field contains text after its closing quote.");
            }
            else Append(character);
        }
        if (quoted) throw new MemberImportException("A CSV field has an unclosed quote.");
        if (field.Length != 0 || row.Count != 0 || closedQuote) { FinishField(); table.AddRow(row); }
        return table.Rows;

        void Append(char character)
        {
            if (field.Length >= MemberImportLimits.FieldCharacters) throw FieldLimit();
            field.Append(character);
        }
        void FinishField()
        {
            if (row.Count >= MemberImportLimits.Columns) throw ColumnLimit();
            row.Add(field.ToString()); field.Clear(); closedQuote = false;
        }
    }

    private static List<List<string>> ReadXml(Stream content, CancellationToken cancellationToken)
    {
        var document = CheckedXml(content, cancellationToken);
        var records = document.Descendants()
            .Where(element => element.Elements().Any() && element.Elements().All(child => !child.Elements().Any()))
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.First().Elements().Count())
            .FirstOrDefault()?.ToList() ?? throw new MemberImportException("The XML file does not contain recognisable member rows.");
        if (records.Count > MemberImportLimits.Members) throw RowLimit();
        var headers = records.SelectMany(record => record.Attributes().Select(attribute => attribute.Name.LocalName)
                .Concat(record.Elements().Select(element => element.Name.LocalName)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MemberImportLimits.Columns + 1).ToList();
        if (headers.Count > MemberImportLimits.Columns) throw ColumnLimit();
        if (headers.Count == 0) throw new MemberImportException("The XML member rows do not contain any fields.");
        var table = new TableBudget();
        table.AddRow(headers);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in record.Attributes()) fields.Add(attribute.Name.LocalName, attribute.Value);
            foreach (var element in record.Elements()) fields.Add(element.Name.LocalName, element.Value);
            table.AddRow(headers.Select(header => fields.GetValueOrDefault(header, string.Empty)).ToList());
        }
        return table.Rows;
    }

    private static List<List<string>> ReadXlsx(Stream content, CancellationToken cancellationToken)
    {
        ValidateArchiveDirectory(content);
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MemberImportLimits.ArchiveEntries)
            throw new MemberImportException("The workbook contains too many components. Export a member-only worksheet.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        long declaredExpansion = 0;
        foreach (var entry in archive.Entries)
        {
            if (!names.Add(entry.FullName)) throw new MemberImportException("The workbook contains duplicate components.");
            declaredExpansion += entry.Length;
            if (declaredExpansion > MemberImportLimits.ExpandedBytes) throw ExpansionLimit();
        }
        long observedExpansion = 0;
        var sharedStrings = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is not null)
        {
            var sharedDocument = Load("xl/sharedStrings.xml");
            var characters = 0;
            foreach (var item in sharedDocument.Descendants(Main + "si"))
            {
                if (sharedStrings.Count >= MemberImportLimits.SharedStrings) throw new MemberImportException("The workbook contains too many shared text values.");
                var value = string.Concat(item.Descendants(Main + "t").Select(node => node.Value));
                if (value.Length > MemberImportLimits.FieldCharacters) throw FieldLimit();
                characters += value.Length;
                if (characters > MemberImportLimits.TableCharacters) throw TextLimit();
                sharedStrings.Add(value);
            }
        }
        var workbook = Load("xl/workbook.xml");
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var relationshipId = workbook.Descendants(Main + "sheet").FirstOrDefault()?.Attribute(rel + "id")?.Value
            ?? throw new MemberImportException("The workbook does not contain a worksheet.");
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationship = Load("xl/_rels/workbook.xml.rels").Descendants(packageRel + "Relationship")
            .FirstOrDefault(node => node.Attribute("Id")?.Value == relationshipId);
        var target = relationship?.Attribute("Target")?.Value
            ?? throw new MemberImportException("The first worksheet could not be opened.");
        target = target.Replace('\\', '/').TrimStart('/');
        if (relationship?.Attribute("TargetMode")?.Value == "External" || target.Contains(':') || target.Split('/').Any(segment => segment is ".." or "."))
            throw new MemberImportException("The workbook must contain its worksheet locally.");
        var sheet = Load(target.StartsWith("xl/", StringComparison.Ordinal) ? target : $"xl/{target}");
        var table = new TableBudget();
        foreach (var rowElement in sheet.Descendants(Main + "row"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements(Main + "c"))
            {
                if (values.Count >= MemberImportLimits.Columns) throw ColumnLimit();
                var index = ColumnIndex(cell.Attribute("r")?.Value ?? "");
                var type = cell.Attribute("t")?.Value;
                var value = type == "inlineStr" ? string.Concat(cell.Descendants(Main + "t").Select(node => node.Value)) : cell.Element(Main + "v")?.Value ?? "";
                if (type == "s")
                {
                    if (!int.TryParse(value, out var sharedIndex) || sharedIndex < 0 || sharedIndex >= sharedStrings.Count)
                        throw new MemberImportException("The workbook references a missing shared text value.");
                    value = sharedStrings[sharedIndex];
                }
                if (!values.TryAdd(index, value)) throw new MemberImportException("The workbook contains duplicate cells in one row.");
            }
            var row = values.Count == 0 ? new List<string>() : Enumerable.Repeat(string.Empty, values.Keys.Max() + 1).ToList();
            foreach (var (index, value) in values) row[index] = value;
            table.AddRow(row);
        }
        return table.Rows;

        XDocument Load(string path)
        {
            var entry = archive.GetEntry(path) ?? throw new MemberImportException("A required workbook component is missing.");
            using var input = entry.Open();
            using var buffered = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observedExpansion += read;
                if (observedExpansion > MemberImportLimits.ExpandedBytes) throw ExpansionLimit();
                buffered.Write(buffer, 0, read);
            }
            if (buffered.Length != entry.Length) throw new MemberImportException("A workbook component has an invalid expanded length.");
            buffered.Position = 0;
            return CheckedXml(buffered, cancellationToken);
        }
    }

    private static void ValidateArchiveDirectory(Stream content)
    {
        // ZipArchive constructs its entire entry collection before Entries.Count can
        // be checked. Inspect the bounded central directory first, including a
        // forged end-record count, so entry objects cannot amplify a small upload.
        var tailLength = (int)Math.Min(content.Length, 65_557);
        if (tailLength < 22) throw new MemberImportException("The workbook is not a valid ZIP archive.");
        var tail = new byte[tailLength];
        content.Position = content.Length - tailLength;
        content.ReadExactly(tail);
        var endIndex = -1;
        for (var index = tailLength - 22; index >= 0; index--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) == 0x06054b50 &&
                index + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(index + 20)) == tailLength)
            { endIndex = index; break; }
        if (endIndex < 0) throw new MemberImportException("The workbook ZIP directory is missing.");
        var end = tail.AsSpan(endIndex);
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
        if (entries > MemberImportLimits.ArchiveEntries) throw new MemberImportException("The workbook contains too many components. Export a member-only worksheet.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != entries)
            throw new MemberImportException("Use a single, standard XLSX workbook archive.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
        var endPosition = content.Length - tailLength + endIndex;
        if ((long)offset + size > endPosition) throw new MemberImportException("The workbook ZIP directory is invalid.");
        content.Position = offset;
        var directoryEnd = (long)offset + size;
        var actualEntries = 0;
        Span<byte> header = stackalloc byte[46];
        while (content.Position < directoryEnd)
        {
            if (++actualEntries > MemberImportLimits.ArchiveEntries)
                throw new MemberImportException("The workbook contains too many components. Export a member-only worksheet.");
            if (directoryEnd - content.Position < header.Length) throw new MemberImportException("The workbook ZIP directory is truncated.");
            content.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
                throw new MemberImportException("The workbook ZIP directory contains an invalid component.");
            var variableLength = (int)BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) +
                BinaryPrimitives.ReadUInt16LittleEndian(header[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            if (content.Position + variableLength > directoryEnd) throw new MemberImportException("The workbook ZIP directory is truncated.");
            content.Position += variableLength;
        }
        if (actualEntries != entries) throw new MemberImportException("The workbook ZIP component count is invalid.");
        content.Position = 0;
    }

    private static XDocument CheckedXml(Stream content, CancellationToken cancellationToken)
    {
        var start = content.Position;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MemberImportLimits.XmlCharacters, CloseInput = false
        };
        // Validate structural bounds before constructing the document tree.
        using (var reader = XmlReader.Create(content, settings))
        {
            var nodes = 0;
            while (reader.Read())
            {
                if ((++nodes & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (nodes > MemberImportLimits.XmlNodes || reader.Depth > MemberImportLimits.XmlDepth)
                    throw new MemberImportException("The XML structure exceeds the safe import limits.");
                if (reader.LocalName.Length > 128 || reader.AttributeCount > MemberImportLimits.Columns) throw ColumnLimit();
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && reader.Value.Length > MemberImportLimits.FieldCharacters) throw FieldLimit();
                if (reader.MoveToFirstAttribute())
                {
                    do { if (reader.Value.Length > MemberImportLimits.FieldCharacters) throw FieldLimit(); } while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }
            }
        }
        content.Position = start;
        using var checkedReader = XmlReader.Create(content, settings);
        return XDocument.Load(checkedReader, LoadOptions.None);
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        var letters = 0;
        while (letters < reference.Length && char.IsAsciiLetter(reference[letters]))
        {
            index = index * 26 + char.ToUpperInvariant(reference[letters]) - 'A' + 1;
            if (index > MemberImportLimits.Columns) throw ColumnLimit();
            letters++;
        }
        if (letters == 0 || letters == reference.Length || !int.TryParse(reference.AsSpan(letters), out var row) || row < 1 || row > 1_048_576)
            throw new MemberImportException("The workbook contains an invalid cell reference.");
        return index - 1;
    }

    private sealed class TableBudget
    {
        public List<List<string>> Rows { get; } = [];
        private int rowCount;
        private int cells;
        private int characters;
        public void AddRow(List<string> row)
        {
            if (++rowCount > MemberImportLimits.Rows) throw RowLimit();
            if (row.Count > MemberImportLimits.Columns) throw ColumnLimit();
            cells += row.Count;
            if (cells > MemberImportLimits.Cells) throw new MemberImportException("The member export contains too many cells. Remove unused columns.");
            foreach (var value in row)
            {
                if (value.Length > MemberImportLimits.FieldCharacters) throw FieldLimit();
                characters += value.Length;
                if (characters > MemberImportLimits.TableCharacters) throw TextLimit();
            }
            if (row.Any(value => !string.IsNullOrWhiteSpace(value))) Rows.Add(row);
        }
    }

    private static MemberImportException FieldLimit() => new("Keep each member export field below 2,049 characters.");
    private static MemberImportException ColumnLimit() => new("Use no more than 64 columns in the member export.");
    private static MemberImportException RowLimit() => new("Import no more than 10,000 members at a time.");
    private static MemberImportException ExpansionLimit() => new("That workbook expands beyond the 24 MB safe import limit.");
    private static MemberImportException TextLimit() => new("The member export contains too much text. Remove unused columns.");
}
