using Microsoft.Extensions.Configuration;
using Trophy.Catalogue.Services;
using Xunit;

namespace Trophy.Catalogue.Tests;

public sealed class SearchDiscoveryTests
{
    [Fact]
    public void UnconfiguredVerificationDoesNotClaimOwnership()
    {
        Assert.Equal("", SearchDiscovery.VerificationTags(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void VerificationValuesCannotInjectHtml()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GOOGLE_SITE_VERIFICATION"] = "  google-proof  ",
            ["BING_SITE_VERIFICATION"] = "\"><script>alert(1)</script>"
        }).Build();
        var tags = SearchDiscovery.VerificationTags(config);
        Assert.Contains("content=\"google-proof\"", tags);
        Assert.Contains("msvalidate.01", tags);
        Assert.DoesNotContain("<script>", tags);
        Assert.Contains("&quot;&gt;&lt;script&gt;", tags);
    }
}
