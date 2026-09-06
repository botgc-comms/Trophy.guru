using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class OperationalSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trophy-ops-test-" + Guid.NewGuid().ToString("N"));
    public OperationalSafetyTests() => Directory.CreateDirectory(root);
    [Fact] public void ASecondApplicationCannotOpenTheSameArchive()
    {
        using var first = new DataDirectoryLease(root);
        Assert.Throws<InvalidOperationException>(() => new DataDirectoryLease(root));
    }
    [Fact] public void BackupCannotRunAgainstAnActiveArchive()
    {
        var data = Path.Combine(root, "active"); using var lease = new DataDirectoryLease(data);
        Assert.Throws<InvalidOperationException>(() => DataMaintenance.Backup(data, Path.Combine(root, "backup")));
        Assert.False(Directory.Exists(Path.Combine(root, "backup")));
    }
    [Fact] public void BackupAndRestoreRetainIdentityDataAndKeysAndNeverOverwrite()
    {
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(Path.Combine(source, "key-ring"));
        File.WriteAllText(Path.Combine(source, "identity.json"), "fixture-account-and-hash");
        File.WriteAllBytes(Path.Combine(source, "key-ring", "key.xml"), [0, 42, 128, 255]);
        var backup = Path.Combine(root, "backup"); var restored = Path.Combine(root, "restored");
        DataMaintenance.Backup(source, backup); DataMaintenance.Restore(backup, restored);
        Assert.Equal(File.ReadAllBytes(Path.Combine(source, "identity.json")), File.ReadAllBytes(Path.Combine(restored, "identity.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(source, "key-ring", "key.xml")), File.ReadAllBytes(Path.Combine(restored, "key-ring", "key.xml")));
        Assert.Throws<IOException>(() => DataMaintenance.Restore(backup, source));
        Assert.Throws<IOException>(() => DataMaintenance.Backup(source, backup));
    }
    [Fact] public void TamperedBackupFailsBeforeCreatingDestination()
    {
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "identity.json"), "fixture");
        var backup = Path.Combine(root, "backup"); var restored = Path.Combine(root, "restored"); DataMaintenance.Backup(source, backup);
        File.WriteAllText(Path.Combine(backup, "identity.json"), "changed");
        Assert.Throws<IOException>(() => DataMaintenance.Restore(backup, restored)); Assert.False(Directory.Exists(restored));
    }
    [Fact] public void NestedBackupDestinationIsRejected()
    {
        Assert.Throws<IOException>(() => DataMaintenance.Backup(root, Path.Combine(root, "nested")));
    }
    [Theory]
    [InlineData("https://archive.example", "same-origin", true)]
    [InlineData("https://attacker.example", "cross-site", false)]
    [InlineData("https://archive.example.attacker.example", "same-site", false)]
    [InlineData("https://archive.example", "cross-site", false)]
    [InlineData("null", "none", false)]
    [InlineData("", "", false)]
    public void MutationOriginIsCheckedAgainstTheConfiguredHttpsSite(string origin, string fetchSite, bool expected)
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/api/trophies";
        context.Request.Scheme = "http"; context.Request.Host = new HostString("internal:10000");
        context.Request.Headers.Origin = origin; context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["PUBLIC_SITE_URL"] = "https://archive.example" }).Build();
        Assert.Equal(expected, RequestSecurity.IsSameOriginMutation(context.Request, config));
    }
    [Fact] public void StripeWebhookReliesOnItsSignatureWithoutBrowserHeaders()
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/api/billing/webhook";
        Assert.True(RequestSecurity.IsSameOriginMutation(context.Request, new ConfigurationBuilder().Build()));
    }
    public void Dispose() { Directory.Delete(root, recursive: true); }
}
