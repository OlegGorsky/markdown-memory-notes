using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncOriginPolicyTests
{
    [Fact]
    public void IsAllowedAllowsAnyOriginWhenAllowlistIsEmpty()
    {
        Assert.True(SyncOriginPolicy.IsAllowed("https://evil.example", []));
    }

    [Theory]
    [InlineData("https://app.example.com", "https://app.example.com")]
    [InlineData("https://APP.example.com", "https://app.example.com")]
    [InlineData("https://app.example.com:443", "https://app.example.com")]
    public void IsAllowedAcceptsExactNormalizedAllowedOrigins(string origin, string allowedOrigin)
    {
        Assert.True(SyncOriginPolicy.IsAllowed(origin, [allowedOrigin]));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://app.example.com.evil.test")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?tab=notes")]
    [InlineData("https://app.example.com#notes")]
    [InlineData("https://user@app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("not-a-url")]
    [InlineData("null")]
    [InlineData("")]
    public void IsAllowedRejectsUnknownOriginsWhenAllowlistIsConfigured(string? origin)
    {
        Assert.False(SyncOriginPolicy.IsAllowed(origin, ["https://app.example.com"]));
    }

    [Fact]
    public void IsAllowedAllowsNonBrowserClientsWithoutOrigin()
    {
        Assert.True(SyncOriginPolicy.IsAllowed(null, ["https://app.example.com"]));
    }
}
