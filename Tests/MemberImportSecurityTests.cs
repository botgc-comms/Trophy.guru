using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Trophy.Catalogue.Domain;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class MemberImportSecurityTests
{
    [Theory]
    [InlineData("ZZZZZZ1")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZ1")]
    [InlineData("BM2")]
    [InlineData("A0")]
    [InlineData("A-1")]
    [InlineData("A")]
    public async Task MalformedOrExcessiveWorksheetReferenceIsRejectedBeforeAllocation(string reference)
    {
        using var fixture = new Fixture();
        using var workbook = Workbook($"<row><c r=\"{reference}\" t=\"inlineStr\"><is><t>Full name</t></is></c></row>");
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Theory]
    [InlineData("xl/sharedStrings.xml")]
    [InlineData("unused/oversized.xml")]
    public async Task ZipExpansionLimitIncludesSharedStringsAndUnusedEntries(string entryName)
    {
        using var fixture = new Fixture();
        using var workbook = new MemoryStream();
        using (var zip = new ZipArchive(workbook, ZipArchiveMode.Create, true))
        {
            using var output = zip.CreateEntry(entryName, CompressionLevel.SmallestSize).Open();
            var zeros = new byte[64 * 1024];
            for (var index = 0; index < 385; index++) output.Write(zeros);
        }
        Assert.True(workbook.Length < 100_000); // Small input; rejection occurs before any expansion.
        workbook.Position = 0;
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Fact]
    public async Task ExcessiveArchiveEntryCountIsRejected()
    {
        using var fixture = new Fixture();
        using var workbook = new MemoryStream();
        using (var zip = new ZipArchive(workbook, ZipArchiveMode.Create, true))
            for (var index = 0; index < 257; index++) zip.CreateEntry($"part-{index}");
        workbook.Position = 0;
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Fact]
    public async Task ForgedSmallArchiveEntryCountCannotHideManyComponents()
    {
        using var fixture = new Fixture();
        using var workbook = new MemoryStream();
        using (var zip = new ZipArchive(workbook, ZipArchiveMode.Create, true))
            for (var index = 0; index < 257; index++) zip.CreateEntry($"part-{index}");
        // Only patch the two end-record counts; the bounded scanner must count
        // actual directory records before ZipArchive allocates an entry list.
        workbook.Position = workbook.Length - 22 + 8;
        workbook.Write(new byte[] { 1, 0, 1, 0 });
        workbook.Position = 0;
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Fact]
    public async Task InvalidSharedStringReferenceIsRejected()
    {
        using var fixture = new Fixture();
        using var workbook = Workbook("<row><c r=\"A1\" t=\"s\"><v>9</v></c></row>");
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Fact]
    public async Task OversizedRichSharedStringIsRejectedAfterConcatenation()
    {
        using var fixture = new Fixture();
        using var workbook = Workbook("<row><c r=\"A1\" t=\"s\"><v>0</v></c></row>",
            "<si><r><t>" + new string('a', 1100) + "</t></r><r><t>" + new string('b', 1100) + "</t></r></si>");
        await fixture.RejectedWithoutChangeAsync("members.xlsx", workbook);
    }

    [Theory]
    [InlineData("members.xml", "<!DOCTYPE members [<!ENTITY x SYSTEM 'file:///does-not-exist'>]><members><member><FullName>&x;</FullName></member></members>")]
    [InlineData("members.xml", "<members><member><FullName>Unclosed")]
    [InlineData("members.csv", "Full name\n\"Unclosed name\n")]
    [InlineData("members.csv", "Full name\n\"Audit name\"unexpected\n")]
    [InlineData("members.json", "[]")]
    public async Task MalformedOrUnsupportedImportsPreserveTheDirectory(string filename, string content)
    {
        using var fixture = new Fixture();
        await fixture.RejectedWithoutChangeAsync(filename, Text(content));
    }

    [Fact]
    public async Task XmlDepthIsBoundedBeforeBuildingTheDocument()
    {
        using var fixture = new Fixture();
        var xml = string.Concat(Enumerable.Repeat("<level>", 40)) + "x" + string.Concat(Enumerable.Repeat("</level>", 40));
        await fixture.RejectedWithoutChangeAsync("members.xml", Text(xml));
    }

    [Fact]
    public async Task CsvFieldsColumnsCellsAndRowCountsAreBounded()
    {
        using var fixture = new Fixture();
        await fixture.RejectedWithoutChangeAsync("members.csv", Text("Full name,Unused\nAudit name," + new string('x', 2049)));
        await fixture.RejectedWithoutChangeAsync("members.csv", Text("Full name," + string.Join(',', Enumerable.Repeat("Unused", 64)) + "\nAudit name"));
        await fixture.RejectedWithoutChangeAsync("members.csv", Text("Full name\n" + string.Concat(Enumerable.Repeat("Audit name\n", 10_001))));
        var wideRow = "Audit name," + string.Join(',', Enumerable.Repeat("x", 63)) + "\n";
        var headers = "Full name," + string.Join(',', Enumerable.Range(0, 63).Select(index => $"Unused{index}")) + "\n";
        await fixture.RejectedWithoutChangeAsync("members.csv", Text(headers + string.Concat(Enumerable.Repeat(wideRow, 3907))));
    }

    [Fact]
    public async Task ImportedNamesAndMembershipNumbersHaveStorageLimits()
    {
        using var fixture = new Fixture();
        await fixture.RejectedWithoutChangeAsync("members.csv", Text("Full name\n" + new string('n', 201)));
        await fixture.RejectedWithoutChangeAsync("members.csv", Text("Full name,Membership number\nAudit name," + new string('9', 81)));
    }

    [Fact]
    public async Task NonSeekableUploadCannotBypassByteLimit()
    {
        using var fixture = new Fixture();
        using var oversized = new RepeatedByteStream(15 * 1024 * 1024 + 1);
        await fixture.RejectedWithoutChangeAsync("members.csv", oversized);
    }

    [Fact]
    public async Task NormalCsvXmlAndXlsxImportsPreserveExpectedMemberValues()
    {
        using var fixture = new Fixture();
        await fixture.Store.ImportAsync("members.csv", Text("Full name,Membership number,Date of birth\r\n\"Ada, Audit\",0007,1970\r\n"));
        var csv = Assert.Single(await fixture.Store.GetMembersAsync());
        Assert.Equal("Ada, Audit", csv.FullName); Assert.Equal("0007", csv.MembershipNumber); Assert.Equal(1970, csv.BirthYear);
        await fixture.Store.ImportAsync("members.xml", Text("<members><member><FullName>Bea Audit</FullName><MembershipNumber>0008</MembershipNumber></member></members>"));
        Assert.Equal("Bea Audit", Assert.Single(await fixture.Store.GetMembersAsync()).FullName);
        using var workbook = Workbook("<row><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>Membership number</t></is></c></row><row><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>0009</t></is></c></row>", "<si><t>Full name</t></si><si><r><t>Clara </t></r><r><t>Audit</t></r></si>");
        await fixture.Store.ImportAsync("members.xlsx", workbook);
        var xlsx = Assert.Single(await fixture.Store.GetMembersAsync());
        Assert.Equal("Clara Audit", xlsx.FullName); Assert.Equal("0009", xlsx.MembershipNumber);
    }

    [Fact]
    public async Task TenThousandMembersAreSupportedAndFurtherManualAdditionPreservesThem()
    {
        using var fixture = new Fixture();
        await fixture.Store.ImportAsync("members.csv", Text(TenThousandMembers()));
        Assert.Equal(10_000, (await fixture.Store.GetSummaryAsync()).MemberCount);
        var before = await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<MemberImportException>(() => fixture.Store.AddManualMemberAsync(new ManualMemberInput("Another audit member", null, null, "extra", null)));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.StatePath));
        Assert.Equal(10_000, (await fixture.Store.GetSummaryAsync()).MemberCount);
    }

    [Fact]
    public async Task ImportCannotDiscardManualMembersToGetBelowTheLimit()
    {
        using var fixture = new Fixture();
        await fixture.RejectedWithoutChangeAsync("members.csv", Text(TenThousandMembers()));
        Assert.Equal("Preserved audit member", Assert.Single(await fixture.Store.GetMembersAsync()).FullName);
    }

    [Fact]
    public async Task ExistingOversizedDirectoryIsRetainedAndReadable()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        await File.WriteAllTextAsync(fixture.StatePath, JsonSerializer.Serialize(new MemberDirectoryState
        {
            SourceName = "Existing fixture", Members = Enumerable.Range(0, 10_001).Select(index => new MemberRecord { FullName = $"Existing audit {index}" }).ToList()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var before = await File.ReadAllBytesAsync(fixture.StatePath);
        Assert.Equal(10_001, (await fixture.Store.GetSummaryAsync()).MemberCount);
        await Assert.ThrowsAsync<MemberImportException>(() => fixture.Store.AddManualMemberAsync(new ManualMemberInput("Another audit member", null, null, "extra", null)));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.StatePath));
    }

    [Fact]
    public async Task OnlyOneImportCanParseAcrossAllClubsAndCancellationReleasesTheSlot()
    {
        using var fixture = new Fixture();
        using var blocked = new PausingStream();
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Store.ImportAsync("members.csv", blocked, cancellation.Token);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (fixture.Context.Push("another-audit-club"))
        {
            var rejected = await Assert.ThrowsAsync<MemberImportException>(() => fixture.Store.ImportAsync("members.csv", Text("Full name\nSecond audit\n")));
            Assert.Contains("in progress", rejected.Message);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await fixture.Store.ImportAsync("members.csv", Text("Full name\nRetry audit\n"));
        Assert.Equal("Retry audit", Assert.Single(await fixture.Store.GetMembersAsync()).FullName);
    }

    [Fact]
    public async Task FailedPersistenceRestoresPreviousInMemoryDirectory()
    {
        using var fixture = new Fixture();
        await fixture.Store.AddManualMemberAsync(new ManualMemberInput("Preserved audit member", null, null, "keep", null));
        var before = (await fixture.Store.GetMembersAsync()).Select(member => member.Id).ToArray();
        File.Delete(fixture.StatePath);
        Directory.CreateDirectory(fixture.StatePath);
        var failure = await Record.ExceptionAsync(() => fixture.Store.ImportAsync("members.csv", Text("Full name\nNew audit member\n")));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(before, (await fixture.Store.GetMembersAsync()).Select(member => member.Id).ToArray());
    }

    private static string TenThousandMembers() => "Full name,Membership number\n" + string.Concat(Enumerable.Range(0, 10_000).Select(index => $"Audit member {index},M{index}\n"));
    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));
    private static MemoryStream Workbook(string rows, string? sharedStrings = null)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Members\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + rows + "</sheetData></worksheet>");
            if (sharedStrings is not null) Add("xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + sharedStrings + "</sst>");
            void Add(string path, string content) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8); writer.Write(content); }
        }
        output.Position = 0;
        return output;
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"trophy-member-import-security-{Guid.NewGuid():N}");
        public ClubContextAccessor Context { get; } = new();
        public MemberDirectoryStore Store { get; }
        public string StatePath => Path.Combine(Root, "clubs", "audit-club", "member-directory.json");
        private readonly IDisposable scope;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            scope = Context.Push("audit-club");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_PATH"] = Root }).Build();
            Store = new MemberDirectoryStore(new TestEnvironment { ContentRootPath = Root }, configuration, Context);
        }
        public async Task RejectedWithoutChangeAsync(string filename, Stream content)
        {
            using (content)
            {
                await Store.AddManualMemberAsync(new ManualMemberInput("Preserved audit member", null, null, "keep", null));
                var before = await File.ReadAllBytesAsync(StatePath);
                await Assert.ThrowsAsync<MemberImportException>(() => Store.ImportAsync(filename, content));
                Assert.Equal(before, await File.ReadAllBytesAsync(StatePath));
                Assert.Single(await Store.GetMembersAsync());
            }
        }
        public void Dispose()
        {
            scope.Dispose();
            var root = Path.GetFullPath(Root);
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("trophy-member-import-security-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture directory.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class RepeatedByteStream(long length) : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, length - position);
            buffer.Span[..count].Fill((byte)'x'); position += count; return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PausingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Trophy.Catalogue.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
