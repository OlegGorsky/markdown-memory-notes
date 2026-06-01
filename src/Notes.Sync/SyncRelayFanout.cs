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

        var broadcastTask = Start(broadcastAsync);
        var backplaneTask = Start(publishBackplaneAsync);

        await Task.WhenAll(broadcastTask, backplaneTask);
        return new SyncRelayFanoutResult(
            await broadcastTask,
            await backplaneTask);
    }

    private static Task<TResult> Start<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Task.FromException<TResult>(exception);
        }
    }
}
