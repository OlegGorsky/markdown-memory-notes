namespace Notes.Sync;

public static class SyncRelayPresenceRefresh
{
    public static bool ShouldBroadcast(SyncRelayFanoutResult delivery)
    {
        return delivery.Broadcast.Failed > 0 ||
               (delivery.Broadcast.Attempted == 0 && delivery.Backplane.RemoteSubscribers == 0);
    }
}
