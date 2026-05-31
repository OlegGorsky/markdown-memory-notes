using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncMetricsTests
{
    [Fact]
    public void SnapshotReflectsRecordedSyncEvents()
    {
        var metrics = new SyncMetrics();

        metrics.MessageReceived();
        metrics.MessageReceived();
        metrics.MessageRejected();
        metrics.MessageRateLimited();
        metrics.ConnectionLimitRejected();
        metrics.JoinTimedOut();
        metrics.DeliveryAttempted(3);
        metrics.DeliverySucceeded();
        metrics.DeliveryFailed();
        metrics.PeerRemoved();

        var snapshot = metrics.Snapshot();

        Assert.Equal(2, snapshot.MessagesReceived);
        Assert.Equal(1, snapshot.MessagesRejected);
        Assert.Equal(1, snapshot.MessagesRateLimited);
        Assert.Equal(1, snapshot.ConnectionLimitRejected);
        Assert.Equal(1, snapshot.JoinTimedOut);
        Assert.Equal(3, snapshot.DeliveriesAttempted);
        Assert.Equal(1, snapshot.DeliveriesSucceeded);
        Assert.Equal(1, snapshot.DeliveriesFailed);
        Assert.Equal(1, snapshot.PeersRemoved);
    }

    [Fact]
    public void RenderPrometheusIncludesRoomStatsAndCounters()
    {
        var metrics = new SyncMetrics();
        metrics.MessageReceived();
        metrics.DeliveryAttempted(2);
        metrics.DeliverySucceeded();

        var text = metrics.RenderPrometheus(new SyncRoomStats(Rooms: 3, Connections: 7), activeWebSockets: 11);

        Assert.Contains("mmn_sync_rooms 3", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_connections 7", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_active_websockets 11", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_messages_received_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_connection_limit_rejected_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_join_timed_out_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_deliveries_attempted_total 2", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_deliveries_succeeded_total 1", text, StringComparison.Ordinal);
    }
}
