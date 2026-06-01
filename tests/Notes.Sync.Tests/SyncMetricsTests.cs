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
        metrics.JoinRejected();
        metrics.DeliveryAttempted(3);
        metrics.DeliverySucceeded();
        metrics.DeliveryFailed();
        metrics.PeerRemoved();
        metrics.PeerCleanupFailed();
        metrics.BackplanePublishAttempted();
        metrics.BackplanePublishSucceeded(remoteSubscribers: 2);
        metrics.BackplanePublishFailed();
        metrics.BackplaneSubscribeAttempted();
        metrics.BackplaneSubscribeSucceeded();
        metrics.BackplaneSubscribeFailed();
        metrics.BackplaneMessageReceived();
        metrics.BackplaneMessageIgnored();
        metrics.BackplaneInvalidPayload();
        metrics.BackplaneReceiveFailed();
        metrics.BackplaneHealthCheckFailed();
        metrics.PresenceTrackerJoinFailed();
        metrics.PresenceTrackerLeaveFailed();
        metrics.PresenceTrackerCountFailed();
        metrics.PresenceTrackerHeartbeatFailed();
        metrics.AdmissionRejected(SyncJoinResult.RoomFull);
        metrics.AdmissionRejected(SyncJoinResult.RoomLimitReached);
        metrics.AdmissionControllerFailed();

        var snapshot = metrics.Snapshot();

        Assert.Equal(2, snapshot.MessagesReceived);
        Assert.Equal(1, snapshot.MessagesRejected);
        Assert.Equal(1, snapshot.MessagesRateLimited);
        Assert.Equal(1, snapshot.ConnectionLimitRejected);
        Assert.Equal(1, snapshot.JoinTimedOut);
        Assert.Equal(1, snapshot.JoinRejected);
        Assert.Equal(3, snapshot.DeliveriesAttempted);
        Assert.Equal(1, snapshot.DeliveriesSucceeded);
        Assert.Equal(1, snapshot.DeliveriesFailed);
        Assert.Equal(1, snapshot.PeersRemoved);
        Assert.Equal(1, snapshot.PeerCleanupFailed);
        Assert.Equal(1, snapshot.BackplanePublishAttempted);
        Assert.Equal(1, snapshot.BackplanePublishSucceeded);
        Assert.Equal(1, snapshot.BackplanePublishFailed);
        Assert.Equal(2, snapshot.BackplaneRemoteSubscribers);
        Assert.Equal(1, snapshot.BackplaneSubscribeAttempted);
        Assert.Equal(1, snapshot.BackplaneSubscribeSucceeded);
        Assert.Equal(1, snapshot.BackplaneSubscribeFailed);
        Assert.Equal(1, snapshot.BackplaneMessagesReceived);
        Assert.Equal(1, snapshot.BackplaneMessagesIgnored);
        Assert.Equal(1, snapshot.BackplaneInvalidPayload);
        Assert.Equal(1, snapshot.BackplaneReceiveFailed);
        Assert.Equal(1, snapshot.BackplaneHealthCheckFailed);
        Assert.Equal(1, snapshot.PresenceTrackerJoinFailed);
        Assert.Equal(1, snapshot.PresenceTrackerLeaveFailed);
        Assert.Equal(1, snapshot.PresenceTrackerCountFailed);
        Assert.Equal(1, snapshot.PresenceTrackerHeartbeatFailed);
        Assert.Equal(1, snapshot.AdmissionRejectedRoomFull);
        Assert.Equal(1, snapshot.AdmissionRejectedRoomLimit);
        Assert.Equal(1, snapshot.AdmissionControllerFailed);
    }

    [Fact]
    public void RenderPrometheusIncludesRoomStatsAndCounters()
    {
        var metrics = new SyncMetrics();
        metrics.MessageReceived();
        metrics.DeliveryAttempted(2);
        metrics.DeliverySucceeded();
        metrics.BackplanePublishAttempted();
        metrics.BackplanePublishSucceeded(remoteSubscribers: 3);
        metrics.BackplaneSubscribeSucceeded();
        metrics.AdmissionRejected(SyncJoinResult.RoomFull);

        var text = metrics.RenderPrometheus(
            new SyncRoomStats(Rooms: 3, Connections: 7),
            activeWebSockets: 11,
            activeBackplaneSubscriptions: 5,
            activeSendGates: 2,
            activeBackplaneReceiveGates: 4);

        Assert.Contains("mmn_sync_rooms 3", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_connections 7", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_active_websockets 11", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_active_backplane_subscriptions 5", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_active_send_gates 2", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_active_backplane_receive_gates 4", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_messages_received_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_connection_limit_rejected_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_join_timed_out_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_join_rejected_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_deliveries_attempted_total 2", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_deliveries_succeeded_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_peer_cleanup_failed_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_publish_attempted_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_publish_succeeded_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_publish_failed_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_remote_subscribers_total 3", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_subscribe_succeeded_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_backplane_health_check_failed_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_presence_tracker_count_failed_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_presence_tracker_heartbeat_failed_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_admission_rejected_room_full_total 1", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_admission_rejected_room_limit_total 0", text, StringComparison.Ordinal);
        Assert.Contains("mmn_sync_admission_controller_failed_total 0", text, StringComparison.Ordinal);
    }
}
