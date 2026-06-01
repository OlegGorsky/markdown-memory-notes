using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRelayFanoutTests
{
    [Fact]
    public async Task DeliverAsyncStartsBackplanePublishBeforeLocalBroadcastCompletes()
    {
        var localStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLocal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backplaneStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var delivery = SyncRelayFanout.DeliverAsync(
            async () =>
            {
                localStarted.SetResult();
                await releaseLocal.Task;
                return new SyncBroadcastResult(Attempted: 1, Succeeded: 1, Failed: 0);
            },
            () =>
            {
                backplaneStarted.SetResult();
                return Task.FromResult(new SyncBackplanePublishResult(Published: true, RemoteSubscribers: 1));
            });

        await localStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var backplaneStartedBeforeLocalCompleted = await Task.WhenAny(
            backplaneStarted.Task,
            Task.Delay(100, TestContext.Current.CancellationToken)) == backplaneStarted.Task;
        releaseLocal.SetResult();

        var result = await delivery;

        Assert.True(backplaneStartedBeforeLocalCompleted);
        Assert.Equal(1, result.Broadcast.Succeeded);
        Assert.Equal(1, result.Backplane.RemoteSubscribers);
    }
}
