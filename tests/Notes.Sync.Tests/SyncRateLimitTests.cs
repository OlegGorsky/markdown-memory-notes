using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRateLimitTests
{
    [Fact]
    public void TryConsumeRejectsMessagesAfterWindowLimit()
    {
        var now = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncRateLimit(2, TimeSpan.FromMinutes(1), () => now);

        Assert.True(limiter.TryConsume());
        Assert.True(limiter.TryConsume());
        Assert.False(limiter.TryConsume());
    }

    [Fact]
    public void TryConsumeAllowsMessagesAfterWindowReset()
    {
        var now = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var limiter = new SyncRateLimit(1, TimeSpan.FromMinutes(1), () => now);
        Assert.True(limiter.TryConsume());

        now = now.AddMinutes(1).AddSeconds(1);

        Assert.True(limiter.TryConsume());
    }

    [Fact]
    public void TryConsumeRejectsBurstAcrossFixedWindowBoundary()
    {
        var start = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var now = start;
        var limiter = new SyncRateLimit(2, TimeSpan.FromMinutes(1), () => now);
        now = start.AddSeconds(59);
        Assert.True(limiter.TryConsume());
        Assert.True(limiter.TryConsume());

        now = start.AddMinutes(1);

        Assert.False(limiter.TryConsume());
    }
}
