using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Notes.Sync;
#pragma warning disable CA1812 // Instantiated via JSON deserialization

var builder = WebApplication.CreateBuilder(args);
var options = SyncServerOptions.FromConfiguration(builder.Configuration);
var app = builder.Build();
var rooms = new SyncRoomRegistry<WebSocket>(options.MaxRooms, options.MaxPeersPerRoom);
var metrics = new SyncMetrics();
var broadcaster = new SyncBroadcaster<WebSocket>(
    rooms,
    static socket => socket.State is WebSocketState.Open,
    SendSocketAsync,
    options.MaxFanoutConcurrency,
    metrics);

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.Map("/sync", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required", context.RequestAborted);
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid();
    string? room = null;

    try
    {
        var joinPayload = await ReceiveTextAsync(ws, options.MaxMessageBytes, context.RequestAborted);
        if (joinPayload is null)
        {
            return;
        }

        if (!SyncJoinRequest.TryGetRoom(joinPayload, out var requestedRoom))
        {
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Invalid room", context.RequestAborted);
            return;
        }

        room = requestedRoom;
        var joinResult = rooms.TryJoin(room, connectionId, ws);
        if (joinResult is not SyncJoinResult.Joined)
        {
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, JoinResultMessage(joinResult), context.RequestAborted);
            return;
        }

        if (app.Logger.IsEnabled(LogLevel.Information))
        {
            SyncLog.PeerConnected(app.Logger, room, rooms.Stats.Connections);
        }
        var rateLimit = new SyncRateLimit(options.MaxMessagesPerMinute, TimeSpan.FromMinutes(1));

        while (ws.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(ws, options.MaxMessageBytes, context.RequestAborted);
            if (message is null)
            {
                break;
            }

            if (!rateLimit.TryConsume())
            {
                metrics.MessageRateLimited();
                await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Rate limit exceeded", context.RequestAborted);
                break;
            }

            if (!SyncRelayMessage.IsValid(message, options.MaxMessageBytes))
            {
                metrics.MessageRejected();
                await CloseSafeAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Invalid sync message", context.RequestAborted);
                break;
            }

            metrics.MessageReceived();
            await broadcaster.BroadcastAsync(room, connectionId, message, options.SendTimeout, app.Logger);
        }
    }
    catch (InvalidDataException exception)
    {
        SyncLog.ProtocolViolation(app.Logger, exception);
        await CloseSafeAsync(ws, WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
    }
    catch (JsonException exception)
    {
        SyncLog.InvalidJsonPayload(app.Logger, exception);
        await CloseSafeAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Invalid payload", CancellationToken.None);
    }
    catch (WebSocketException exception)
    {
        SyncLog.SocketClosedUnexpectedly(app.Logger, exception);
    }
    catch (OperationCanceledException)
    {
        SyncLog.RequestCancelled(app.Logger);
    }
    finally
    {
        if (room is not null)
        {
            rooms.Leave(room, connectionId);
            if (app.Logger.IsEnabled(LogLevel.Information))
            {
                SyncLog.PeerDisconnected(app.Logger, room, rooms.Stats.Connections);
            }
        }
    }
});

app.MapGet("/health", () =>
{
    var stats = rooms.Stats;
    var counters = metrics.Snapshot();
    return Results.Ok(new
    {
        status = "ok",
        rooms = stats.Rooms,
        connections = stats.Connections,
        counters,
        options.MaxPeersPerRoom,
        options.MaxMessageBytes,
        options.MaxMessagesPerMinute,
        options.MaxFanoutConcurrency
    });
});

app.MapGet("/metrics", () =>
{
    return Results.Text(metrics.RenderPrometheus(rooms.Stats), "text/plain; version=0.0.4");
});

app.Run(Environment.GetEnvironmentVariable("MMN_SYNC_URL") ?? "http://0.0.0.0:5199");

static async Task<string?> ReceiveTextAsync(WebSocket ws, int maxBytes, CancellationToken cancellationToken)
{
    var buffer = new byte[Math.Min(maxBytes, 16 * 1024)];
    using var stream = new MemoryStream();

    while (true)
    {
        var result = await ws.ReceiveAsync(buffer, cancellationToken);
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

static Task SendSocketAsync(WebSocket socket, string message, CancellationToken cancellationToken)
{
    var payload = Encoding.UTF8.GetBytes(message);
    return socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
}

static async Task CloseSafeAsync(WebSocket ws, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
{
    if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
    {
        await ws.CloseAsync(status, description, cancellationToken);
    }
}

static string JoinResultMessage(SyncJoinResult result)
{
    return result switch
    {
        SyncJoinResult.RoomLimitReached => "Room limit reached",
        SyncJoinResult.RoomFull => "Room is full",
        SyncJoinResult.InvalidRoom => "Invalid room",
        _ => "Join rejected"
    };
}
