using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncForwardedHeadersPolicyTests
{
    [Fact]
    public void IsConfiguredIsFalseWhenNoTrustedForwardedHeaderSourceIsConfigured()
    {
        Assert.False(SyncForwardedHeadersPolicy.IsConfigured(SyncServerOptions.Default));
    }

    [Fact]
    public void IsConfiguredIsFalseWhenOnlyInvalidTrustedForwardedHeaderSourcesAreConfigured()
    {
        var options = SyncServerOptions.Default with
        {
            TrustedProxies = ["not-an-ip"],
            TrustedNetworks = ["bad-cidr"]
        };

        Assert.False(SyncForwardedHeadersPolicy.IsConfigured(options));
    }

    [Fact]
    public void CreateBuildsForwardedForPolicyForTrustedProxiesAndNetworks()
    {
        var options = SyncServerOptions.Default with
        {
            TrustedProxies = ["127.0.0.1"],
            TrustedNetworks = ["10.10.0.0/16"]
        };

        var forwarded = SyncForwardedHeadersPolicy.Create(options);

        Assert.Equal(ForwardedHeaders.XForwardedFor, forwarded.ForwardedHeaders);
        Assert.Equal(1, forwarded.ForwardLimit);
        Assert.Contains(IPAddress.Parse("127.0.0.1"), forwarded.KnownProxies);
        var network = Assert.Single(forwarded.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("10.10.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }

    [Fact]
    public void CreateIgnoresInvalidTrustedForwardedHeaderSources()
    {
        var options = SyncServerOptions.Default with
        {
            TrustedProxies = ["not-an-ip", "127.0.0.1"],
            TrustedNetworks = ["bad-cidr", "10.20.0.0/16"]
        };

        var forwarded = SyncForwardedHeadersPolicy.Create(options);

        Assert.Equal([IPAddress.Parse("127.0.0.1")], forwarded.KnownProxies);
        var network = Assert.Single(forwarded.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("10.20.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }
}
