using System.Net.WebSockets;

namespace Notes.Sync;

public enum SyncJoinPayloadStatus
{
    Received,
    Closed,
    TimedOut
}

public sealed record SyncJoinPayloadResult(SyncJoinPayloadStatus Status, string? Payload)
{
    public static SyncJoinPayloadResult Received(string payload) => new(SyncJoinPayloadStatus.Received, payload);
    public static SyncJoinPayloadResult Closed { get; } = new(SyncJoinPayloadStatus.Closed, null);
    public static SyncJoinPayloadResult TimedOut { get; } = new(SyncJoinPayloadStatus.TimedOut, null);
}

public static class SyncJoinPayloadReceiver
{
    public static async Task<SyncJoinPayloadResult> ReceiveAsync(
        Func<CancellationToken, Task<string?>> receiveAsync,
        TimeSpan joinTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiveAsync);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(joinTimeout, TimeSpan.Zero);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<string?> receiveTask;
        try
        {
            receiveTask = receiveAsync(timeout.Token);
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return SyncJoinPayloadResult.TimedOut;
        }

        var timeoutTask = Task.Delay(joinTimeout, cancellationToken);

        if (await Task.WhenAny(receiveTask, timeoutTask) == timeoutTask)
        {
            await timeoutTask;
            await timeout.CancelAsync();
            ObserveFault(receiveTask);
            return SyncJoinPayloadResult.TimedOut;
        }

        try
        {
            var payload = await receiveTask;
            return payload is null
                ? SyncJoinPayloadResult.Closed
                : SyncJoinPayloadResult.Received(payload);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return SyncJoinPayloadResult.TimedOut;
        }
        catch (WebSocketException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return SyncJoinPayloadResult.TimedOut;
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return SyncJoinPayloadResult.TimedOut;
        }
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
