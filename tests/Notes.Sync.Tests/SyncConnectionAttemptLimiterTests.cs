using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncConnectionAttemptLimiterTests
{
    [Fact]
    public void TryConsumeRejectsAttemptsAfterWindowLimit()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncConnectionAttemptLimiter(2, TimeSpan.FromMinutes(1), () => now);

        Assert.True(limiter.TryConsume("client-a"));
        Assert.True(limiter.TryConsume("client-a"));
        Assert.False(limiter.TryConsume("client-a"));
    }

    [Fact]
    public void TryConsumeAllowsAttemptsAfterWindowReset()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncConnectionAttemptLimiter(1, TimeSpan.FromMinutes(1), () => now);
        Assert.True(limiter.TryConsume("client-a"));

        now = now.AddMinutes(1).AddSeconds(1);

        Assert.True(limiter.TryConsume("client-a"));
    }

    [Fact]
    public void TryConsumeTreatsIpv4MappedIpv6AsSameClient()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncConnectionAttemptLimiter(1, TimeSpan.FromMinutes(1), () => now);

        Assert.True(limiter.TryConsume("192.0.2.1"));
        Assert.False(limiter.TryConsume("::ffff:192.0.2.1"));
    }

    [Fact]
    public void TryConsumePrunesInactiveClientsAfterWindow()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncConnectionAttemptLimiter(1, TimeSpan.FromMinutes(1), () => now);
        Assert.True(limiter.TryConsume("client-a"));
        Assert.Equal(1, limiter.TrackedClientCount);

        now = now.AddMinutes(1).AddSeconds(1);
        Assert.True(limiter.TryConsume("client-b"));

        Assert.Equal(1, limiter.TrackedClientCount);
    }

    [Fact]
    public void TryConsumeRejectsNewClientsWhenTrackedClientLimitIsReached()
    {
        var limiter = new SyncConnectionAttemptLimiter(
            limit: 2,
            window: TimeSpan.FromMinutes(1),
            maxTrackedClients: 1);

        Assert.True(limiter.TryConsume("client-a"));
        Assert.True(limiter.TryConsume("client-a"));
        Assert.False(limiter.TryConsume("client-b"));
        Assert.Equal(1, limiter.TrackedClientCount);
    }
}
