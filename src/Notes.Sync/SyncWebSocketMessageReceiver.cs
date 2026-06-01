using System.Net.WebSockets;
using System.Text;

namespace Notes.Sync;

public static class SyncWebSocketMessageReceiver
{
    public static async Task<string?> ReceiveTextAsync(
        WebSocket socket,
        int maxBytes,
        TimeSpan receiveTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(receiveTimeout, TimeSpan.Zero);

        var buffer = new byte[Math.Min(maxBytes, 16 * 1024)];
        using var stream = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(receiveTimeout);

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException("Only text messages are supported.");
                }

                if (stream.Length + result.Count > maxBytes)
                {
                    throw new InvalidDataException("Message exceeds configured size limit.");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while receiving a WebSocket message.", exception);
        }
    }
}
