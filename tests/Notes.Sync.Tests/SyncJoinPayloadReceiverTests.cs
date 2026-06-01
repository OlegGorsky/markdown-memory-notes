using System.Net.WebSockets;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncJoinPayloadReceiverTests
{
    [Fact]
    public async Task ReceiveAsyncReturnsPayloadBeforeTimeout()
    {
        var result = await SyncJoinPayloadReceiver.ReceiveAsync(
            _ => Task.FromResult<string?>("""{"room":"AbCdEfGhIjKlMnOpQrStUv"}"""),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(SyncJoinPayloadStatus.Received, result.Status);
        Assert.Equal("""{"room":"AbCdEfGhIjKlMnOpQrStUv"}""", result.Payload);
    }

    [Fact]
    public async Task ReceiveAsyncReturnsTimedOutWhenReceiveIsCancelledByJoinDeadline()
    {
        var result = await SyncJoinPayloadReceiver.ReceiveAsync(
            token => WaitForCancellationAsync(throwSocketException: false, token),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.Equal(SyncJoinPayloadStatus.TimedOut, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ReceiveAsyncReturnsTimedOutWhenSocketAbortsOnJoinDeadline()
    {
        var result = await SyncJoinPayloadReceiver.ReceiveAsync(
            token => WaitForCancellationAsync(throwSocketException: true, token),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.Equal(SyncJoinPayloadStatus.TimedOut, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ReceiveAsyncReturnsTimedOutWhenReceiveReportsTimeout()
    {
        var result = await SyncJoinPayloadReceiver.ReceiveAsync(
            _ => throw new TimeoutException("receive timeout"),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(SyncJoinPayloadStatus.TimedOut, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ReceiveAsyncPropagatesRequestCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SyncJoinPayloadReceiver.ReceiveAsync(
                token => WaitForCancellationAsync(throwSocketException: false, token),
                TimeSpan.FromSeconds(1),
                cancellation.Token));
    }

    private static async Task<string?> WaitForCancellationAsync(bool throwSocketException, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (throwSocketException)
        {
            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        }
    }
}
