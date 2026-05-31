using System.Globalization;

namespace Notes.Sync;

public sealed class SyncMetrics
{
    private long messagesReceived;
    private long messagesRejected;
    private long messagesRateLimited;
    private long connectionLimitRejected;
    private long joinTimedOut;
    private long joinRejected;
    private long deliveriesAttempted;
    private long deliveriesSucceeded;
    private long deliveriesFailed;
    private long peersRemoved;
    private long backplanePublishAttempted;
    private long backplanePublishSucceeded;
    private long backplanePublishFailed;
    private long backplaneRemoteSubscribers;
    private long backplaneSubscribeAttempted;
    private long backplaneSubscribeSucceeded;
    private long backplaneSubscribeFailed;
    private long backplaneMessagesReceived;
    private long backplaneMessagesIgnored;
    private long backplaneInvalidPayload;
    private long backplaneReceiveFailed;

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

    public void JoinRejected()
    {
        Interlocked.Increment(ref joinRejected);
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

    public void BackplanePublishAttempted()
    {
        Interlocked.Increment(ref backplanePublishAttempted);
    }

    public void BackplanePublishSucceeded(int remoteSubscribers)
    {
        Interlocked.Increment(ref backplanePublishSucceeded);
        if (remoteSubscribers > 0)
        {
            Interlocked.Add(ref backplaneRemoteSubscribers, remoteSubscribers);
        }
    }

    public void BackplanePublishFailed()
    {
        Interlocked.Increment(ref backplanePublishFailed);
    }

    public void BackplaneSubscribeAttempted()
    {
        Interlocked.Increment(ref backplaneSubscribeAttempted);
    }

    public void BackplaneSubscribeSucceeded()
    {
        Interlocked.Increment(ref backplaneSubscribeSucceeded);
    }

    public void BackplaneSubscribeFailed()
    {
        Interlocked.Increment(ref backplaneSubscribeFailed);
    }

    public void BackplaneMessageReceived()
    {
        Interlocked.Increment(ref backplaneMessagesReceived);
    }

    public void BackplaneMessageIgnored()
    {
        Interlocked.Increment(ref backplaneMessagesIgnored);
    }

    public void BackplaneInvalidPayload()
    {
        Interlocked.Increment(ref backplaneInvalidPayload);
    }

    public void BackplaneReceiveFailed()
    {
        Interlocked.Increment(ref backplaneReceiveFailed);
    }

    public SyncMetricSnapshot Snapshot()
    {
        return new SyncMetricSnapshot(
            Interlocked.Read(ref messagesReceived),
            Interlocked.Read(ref messagesRejected),
            Interlocked.Read(ref messagesRateLimited),
            Interlocked.Read(ref connectionLimitRejected),
            Interlocked.Read(ref joinTimedOut),
            Interlocked.Read(ref joinRejected),
            Interlocked.Read(ref deliveriesAttempted),
            Interlocked.Read(ref deliveriesSucceeded),
            Interlocked.Read(ref deliveriesFailed),
            Interlocked.Read(ref peersRemoved),
            Interlocked.Read(ref backplanePublishAttempted),
            Interlocked.Read(ref backplanePublishSucceeded),
            Interlocked.Read(ref backplanePublishFailed),
            Interlocked.Read(ref backplaneRemoteSubscribers),
            Interlocked.Read(ref backplaneSubscribeAttempted),
            Interlocked.Read(ref backplaneSubscribeSucceeded),
            Interlocked.Read(ref backplaneSubscribeFailed),
            Interlocked.Read(ref backplaneMessagesReceived),
            Interlocked.Read(ref backplaneMessagesIgnored),
            Interlocked.Read(ref backplaneInvalidPayload),
            Interlocked.Read(ref backplaneReceiveFailed));
    }

    public string RenderPrometheus(
        SyncRoomStats stats,
        int activeWebSockets,
        int activeBackplaneSubscriptions = 0)
    {
        var snapshot = Snapshot();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            mmn_sync_rooms {stats.Rooms}
            mmn_sync_connections {stats.Connections}
            mmn_sync_active_websockets {activeWebSockets}
            mmn_sync_active_backplane_subscriptions {activeBackplaneSubscriptions}
            mmn_sync_messages_received_total {snapshot.MessagesReceived}
            mmn_sync_messages_rejected_total {snapshot.MessagesRejected}
            mmn_sync_messages_rate_limited_total {snapshot.MessagesRateLimited}
            mmn_sync_connection_limit_rejected_total {snapshot.ConnectionLimitRejected}
            mmn_sync_join_timed_out_total {snapshot.JoinTimedOut}
            mmn_sync_join_rejected_total {snapshot.JoinRejected}
            mmn_sync_deliveries_attempted_total {snapshot.DeliveriesAttempted}
            mmn_sync_deliveries_succeeded_total {snapshot.DeliveriesSucceeded}
            mmn_sync_deliveries_failed_total {snapshot.DeliveriesFailed}
            mmn_sync_peers_removed_total {snapshot.PeersRemoved}
            mmn_sync_backplane_publish_attempted_total {snapshot.BackplanePublishAttempted}
            mmn_sync_backplane_publish_succeeded_total {snapshot.BackplanePublishSucceeded}
            mmn_sync_backplane_publish_failed_total {snapshot.BackplanePublishFailed}
            mmn_sync_backplane_remote_subscribers_total {snapshot.BackplaneRemoteSubscribers}
            mmn_sync_backplane_subscribe_attempted_total {snapshot.BackplaneSubscribeAttempted}
            mmn_sync_backplane_subscribe_succeeded_total {snapshot.BackplaneSubscribeSucceeded}
            mmn_sync_backplane_subscribe_failed_total {snapshot.BackplaneSubscribeFailed}
            mmn_sync_backplane_messages_received_total {snapshot.BackplaneMessagesReceived}
            mmn_sync_backplane_messages_ignored_total {snapshot.BackplaneMessagesIgnored}
            mmn_sync_backplane_invalid_payload_total {snapshot.BackplaneInvalidPayload}
            mmn_sync_backplane_receive_failed_total {snapshot.BackplaneReceiveFailed}

            """);
    }
}

public sealed record SyncMetricSnapshot(
    long MessagesReceived,
    long MessagesRejected,
    long MessagesRateLimited,
    long ConnectionLimitRejected,
    long JoinTimedOut,
    long JoinRejected,
    long DeliveriesAttempted,
    long DeliveriesSucceeded,
    long DeliveriesFailed,
    long PeersRemoved,
    long BackplanePublishAttempted,
    long BackplanePublishSucceeded,
    long BackplanePublishFailed,
    long BackplaneRemoteSubscribers,
    long BackplaneSubscribeAttempted,
    long BackplaneSubscribeSucceeded,
    long BackplaneSubscribeFailed,
    long BackplaneMessagesReceived,
    long BackplaneMessagesIgnored,
    long BackplaneInvalidPayload,
    long BackplaneReceiveFailed);
