namespace Notes.Sync;

public sealed record SyncRelayFanoutResult(
    SyncBroadcastResult Broadcast,
    SyncBackplanePublishResult Backplane);

public static class SyncRelayFanout
{
    public static async Task<SyncRelayFanoutResult> DeliverAsync(
        Func<Task<SyncBroadcastResult>> broadcastAsync,
        Func<Task<SyncBackplanePublishResult>> publishBackplaneAsync)
    {
        ArgumentNullException.ThrowIfNull(broadcastAsync);
        ArgumentNullException.ThrowIfNull(publishBackplaneAsync);

        var broadcastTask = broadcastAsync();
        var backplaneTask = publishBackplaneAsync();

        await Task.WhenAll(broadcastTask, backplaneTask);
        return new SyncRelayFanoutResult(
            await broadcastTask,
            await backplaneTask);
    }
}
