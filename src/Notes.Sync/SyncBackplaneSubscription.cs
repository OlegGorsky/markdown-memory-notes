using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public sealed class SyncBackplaneSubscription : IDisposable, IAsyncDisposable
{
    private readonly string room;
    private readonly Func<string, CancellationToken, Task> onPayloadAsync;
    private readonly SyncMetrics metrics;
    private readonly ILogger logger;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationToken stopToken;
    private readonly Channel<string> queue;
    private readonly Task completion;
    private int disposed;
    private int sourceDisposalScheduled;

    public SyncBackplaneSubscription(
        string room,
        int capacity,
        Func<string, CancellationToken, Task> onPayloadAsync,
        SyncMetrics metrics,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(onPayloadAsync);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        this.room = room;
        this.onPayloadAsync = onPayloadAsync;
        this.metrics = metrics;
        this.logger = logger;
        stopToken = stop.Token;
        queue = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        completion = RunAsync();
    }

    public Task Completion => completion;

    public bool TryEnqueue(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (Volatile.Read(ref disposed) == 1)
        {
            return false;
        }

        if (queue.Writer.TryWrite(payload))
        {
            return true;
        }

        metrics.BackplaneReceiveDropped();
        SyncLog.BackplaneReceiveQueueFull(logger, room);
        return false;
    }

    public void Dispose()
    {
        if (TryStop())
        {
            DisposeSourceWhenComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        TryStop();
        try
        {
            await completion.ConfigureAwait(false);
        }
        finally
        {
            DisposeSource();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var payload in queue.Reader.ReadAllAsync(stopToken))
            {
                try
                {
                    await onPayloadAsync(payload, stopToken);
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    break;
                }
                catch (JsonException exception)
                {
                    metrics.BackplaneInvalidPayload();
                    SyncLog.BackplaneInvalidPayload(logger, exception, room);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    metrics.BackplaneReceiveFailed();
                    SyncLog.BackplaneReceiveFailed(logger, exception, room);
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
        }
    }

    private bool TryStop()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return false;
        }

        queue.Writer.TryComplete();
        stop.Cancel();
        return true;
    }

    private void DisposeSourceWhenComplete()
    {
        if (Interlocked.Exchange(ref sourceDisposalScheduled, 1) == 1)
        {
            return;
        }

        _ = completion.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            stop,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void DisposeSource()
    {
        if (Interlocked.Exchange(ref sourceDisposalScheduled, 1) == 0)
        {
            stop.Dispose();
        }
    }
}
