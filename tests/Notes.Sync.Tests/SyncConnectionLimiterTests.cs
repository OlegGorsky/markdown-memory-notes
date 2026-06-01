using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncConnectionLimiterTests
{
    [Fact]
    public void TryAcquireRejectsWhenTotalConnectionLimitIsReached()
    {
        var limiter = new SyncConnectionLimiter(maxConnections: 1, maxConnectionsPerKey: 10);

        using var first = limiter.TryAcquire("client-a");
        using var second = limiter.TryAcquire("client-b");

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);
    }

    [Fact]
    public void TryAcquireRejectsWhenPerKeyConnectionLimitIsReached()
    {
        var limiter = new SyncConnectionLimiter(maxConnections: 10, maxConnectionsPerKey: 1);

        using var first = limiter.TryAcquire("client-a");
        using var second = limiter.TryAcquire("client-a");

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);
    }

    [Fact]
    public void TryAcquireTreatsIpv4MappedIpv6AsSameClient()
    {
        var limiter = new SyncConnectionLimiter(maxConnections: 10, maxConnectionsPerKey: 1);

        using var first = limiter.TryAcquire("192.0.2.1");
        using var second = limiter.TryAcquire("::ffff:192.0.2.1");

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);

        first.Dispose();
        using var third = limiter.TryAcquire("::ffff:192.0.2.1");

        Assert.True(third.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);
    }

    [Fact]
    public void DisposingLeaseReleasesConnectionSlot()
    {
        var limiter = new SyncConnectionLimiter(maxConnections: 1, maxConnectionsPerKey: 1);

        var first = limiter.TryAcquire("client-a");
        first.Dispose();
        using var second = limiter.TryAcquire("client-a");

        Assert.True(first.Acquired);
        Assert.True(second.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);
    }

    [Fact]
    public void DisposingRejectedLeaseDoesNotChangeActiveConnections()
    {
        var limiter = new SyncConnectionLimiter(maxConnections: 1, maxConnectionsPerKey: 1);

        using var first = limiter.TryAcquire("client-a");
        var rejected = limiter.TryAcquire("client-b");
        rejected.Dispose();

        Assert.True(first.Acquired);
        Assert.False(rejected.Acquired);
        Assert.Equal(1, limiter.ActiveConnections);
    }
}
