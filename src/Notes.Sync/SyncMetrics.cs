using System.Globalization;

namespace Notes.Sync;

public sealed class SyncMetrics
{
    private long messagesReceived;
    private long messagesRejected;
    private long messagesRateLimited;
    private long connectionLimitRejected;
    private long joinTimedOut;
    private long deliveriesAttempted;
    private long deliveriesSucceeded;
    private long deliveriesFailed;
    private long peersRemoved;

    public void MessageReceived()
    {
        Interlocked.Increment(ref messagesReceived);
    }

    public void MessageRejected()
    {
        Interlocked.Increment(ref messagesRejected);
    }

    public void MessageRateLimited()
    {
        Interlocked.Increment(ref messagesRateLimited);
    }

    public void ConnectionLimitRejected()
    {
        Interlocked.Increment(ref connectionLimitRejected);
    }

    public void JoinTimedOut()
    {
        Interlocked.Increment(ref joinTimedOut);
    }

    public void DeliveryAttempted(int count = 1)
    {
        if (count > 0)
        {
            Interlocked.Add(ref deliveriesAttempted, count);
        }
    }

    public void DeliverySucceeded()
    {
        Interlocked.Increment(ref deliveriesSucceeded);
    }

    public void DeliveryFailed()
    {
        Interlocked.Increment(ref deliveriesFailed);
    }

    public void PeerRemoved()
    {
        Interlocked.Increment(ref peersRemoved);
    }

    public SyncMetricSnapshot Snapshot()
    {
        return new SyncMetricSnapshot(
            Interlocked.Read(ref messagesReceived),
            Interlocked.Read(ref messagesRejected),
            Interlocked.Read(ref messagesRateLimited),
            Interlocked.Read(ref connectionLimitRejected),
            Interlocked.Read(ref joinTimedOut),
            Interlocked.Read(ref deliveriesAttempted),
            Interlocked.Read(ref deliveriesSucceeded),
            Interlocked.Read(ref deliveriesFailed),
            Interlocked.Read(ref peersRemoved));
    }

    public string RenderPrometheus(SyncRoomStats stats, int activeWebSockets)
    {
        var snapshot = Snapshot();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            mmn_sync_rooms {stats.Rooms}
            mmn_sync_connections {stats.Connections}
            mmn_sync_active_websockets {activeWebSockets}
            mmn_sync_messages_received_total {snapshot.MessagesReceived}
            mmn_sync_messages_rejected_total {snapshot.MessagesRejected}
            mmn_sync_messages_rate_limited_total {snapshot.MessagesRateLimited}
            mmn_sync_connection_limit_rejected_total {snapshot.ConnectionLimitRejected}
            mmn_sync_join_timed_out_total {snapshot.JoinTimedOut}
            mmn_sync_deliveries_attempted_total {snapshot.DeliveriesAttempted}
            mmn_sync_deliveries_succeeded_total {snapshot.DeliveriesSucceeded}
            mmn_sync_deliveries_failed_total {snapshot.DeliveriesFailed}
            mmn_sync_peers_removed_total {snapshot.PeersRemoved}

            """);
    }
}

public sealed record SyncMetricSnapshot(
    long MessagesReceived,
    long MessagesRejected,
    long MessagesRateLimited,
    long ConnectionLimitRejected,
    long JoinTimedOut,
    long DeliveriesAttempted,
    long DeliveriesSucceeded,
    long DeliveriesFailed,
    long PeersRemoved);
