using Microsoft.Extensions.Logging.Abstractions;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneSubscriptionTests
{
    private const string Room = "RoomBackplaneQueue-ABCDEFGH";

    [Fact]
    public async Task TryEnqueueDropsMessagesWhenReceiveQueueIsFull()
    {
        var metrics = new SyncMetrics();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = new SyncBackplaneSubscription(
            Room,
            capacity: 1,
            async (payload, cancellationToken) =>
            {
                if (payload == "first")
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else if (payload == "second")
                {
                    secondHandled.SetResult();
                }
            },
            metrics,
            NullLogger.Instance);

        Assert.True(subscription.TryEnqueue("first"));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(subscription.TryEnqueue("second"));

        Assert.False(subscription.TryEnqueue("third"));
        Assert.Equal(1, metrics.Snapshot().BackplaneReceiveDropped);

        releaseFirst.SetResult();
        await secondHandled.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }
}
