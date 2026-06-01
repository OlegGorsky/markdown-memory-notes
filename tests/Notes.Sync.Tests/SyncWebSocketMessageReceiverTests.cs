using System.Net.WebSockets;
using System.Text;
using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncWebSocketMessageReceiverTests
{
    [Fact]
    public async Task ReceiveTextAsyncReadsFragmentedTextWithinLimit()
    {
        using var socket = ScriptedWebSocket.WithTextFrames(
            ("{\"room\":\"", EndOfMessage: false),
            ("AbCdEfGhIjKlMnOpQrStUv\"}", EndOfMessage: true));

        var text = await SyncWebSocketMessageReceiver.ReceiveTextAsync(
            socket,
            maxBytes: 128,
            receiveTimeout: TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal("""{"room":"AbCdEfGhIjKlMnOpQrStUv"}""", text);
    }

    [Fact]
    public async Task ReceiveTextAsyncTimesOutWhenMessageDoesNotComplete()
    {
        using var socket = ScriptedWebSocket.Hanging();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            SyncWebSocketMessageReceiver.ReceiveTextAsync(
                socket,
                maxBytes: 128,
                receiveTimeout: TimeSpan.FromMilliseconds(20),
                CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveTextAsyncRejectsOversizedFragmentedMessages()
    {
        using var socket = ScriptedWebSocket.WithTextFrames(
            ("12345", EndOfMessage: false),
            ("67890", EndOfMessage: true));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyncWebSocketMessageReceiver.ReceiveTextAsync(
                socket,
                maxBytes: 8,
                receiveTimeout: TimeSpan.FromSeconds(1),
                CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveTextAsyncReturnsNullWhenPeerCloses()
    {
        using var socket = ScriptedWebSocket.WithCloseFrame();

        var text = await SyncWebSocketMessageReceiver.ReceiveTextAsync(
            socket,
            maxBytes: 128,
            receiveTimeout: TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Null(text);
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<Frame> frames;
        private readonly bool hang;

        private ScriptedWebSocket(IEnumerable<Frame> frames, bool hang = false)
        {
            this.frames = new Queue<Frame>(frames);
            this.hang = hang;
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State { get; } = WebSocketState.Open;
        public override string? SubProtocol => null;

        public static ScriptedWebSocket Hanging()
        {
            return new ScriptedWebSocket([], hang: true);
        }

        public static ScriptedWebSocket WithCloseFrame()
        {
            return new ScriptedWebSocket([new Frame([], WebSocketMessageType.Close, EndOfMessage: true)]);
        }

        public static ScriptedWebSocket WithTextFrames(params (string Text, bool EndOfMessage)[] frames)
        {
            return new ScriptedWebSocket(frames.Select(frame =>
                new Frame(Encoding.UTF8.GetBytes(frame.Text), WebSocketMessageType.Text, frame.EndOfMessage)));
        }

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var frame = frames.Dequeue();
            frame.Payload.CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(frame.Payload.Length, frame.Type, frame.EndOfMessage);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private sealed record Frame(byte[] Payload, WebSocketMessageType Type, bool EndOfMessage);
    }
}
