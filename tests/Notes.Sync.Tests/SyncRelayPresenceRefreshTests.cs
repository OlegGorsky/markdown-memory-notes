using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRelayPresenceRefreshTests
{
    [Theory]
    [InlineData(0, 0, 2, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(2, 1, 0, true)]
    [InlineData(2, 0, 0, false)]
    public void ShouldBroadcastOnlyWhenLocalPeersFailedOrNoRecipientsWereReached(
        int localAttempted,
        int localFailed,
        int remoteSubscribers,
        bool expected)
    {
        var delivery = new SyncRelayFanoutResult(
            new SyncBroadcastResult(
                Attempted: localAttempted,
                Succeeded: Math.Max(0, localAttempted - localFailed),
                Failed: localFailed),
            new SyncBackplanePublishResult(
                Published: remoteSubscribers > 0,
                RemoteSubscribers: remoteSubscribers));

        Assert.Equal(expected, SyncRelayPresenceRefresh.ShouldBroadcast(delivery));
    }
}
