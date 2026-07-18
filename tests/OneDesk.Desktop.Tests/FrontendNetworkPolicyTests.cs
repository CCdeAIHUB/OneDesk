using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class FrontendNetworkPolicyTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("ws://example.com")]
    [InlineData("wss://example.com")]
    public void DirectRemoteProtocols_AreBlocked(string address)
    {
        var policy = new FrontendNetworkPolicy();

        Assert.True(policy.ShouldBlock(new Uri(address)));
    }

    [Fact]
    public void BundledFileResource_IsAllowed()
    {
        var policy = new FrontendNetworkPolicy();

        Assert.False(policy.ShouldBlock(new Uri("file:///opt/onedesk/wwwroot/index.html")));
    }
}
