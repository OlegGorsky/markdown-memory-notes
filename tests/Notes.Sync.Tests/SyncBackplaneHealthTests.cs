using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneHealthTests
{
    [Fact]
    public async Task NoopBackplaneReportsDisabledHealthyStatus()
    {
        var health = await NoopSyncBackplane.Instance.CheckHealthAsync(CancellationToken.None);

        Assert.False(health.Enabled);
        Assert.True(health.Healthy);
        Assert.Null(health.LatencyMilliseconds);
        Assert.Equal("disabled", health.Status);
    }
}
