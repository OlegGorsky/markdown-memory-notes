using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Notes.Sync;
#pragma warning disable CA1812 // Instantiated via JSON deserialization

var builder = WebApplication.CreateBuilder(args);
var options = SyncServerOptions.FromConfiguration(builder.Configuration);
var app = builder.Build();
var rooms = new SyncRoomRegistry<WebSocket>(options.MaxRooms, options.MaxPeersPerRoom);

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

        var join = JsonSerializer.Deserialize<JoinMessage>(joinPayload);
        var requestedRoom = join?.Room;
        if (!SyncRoomCode.IsValid(requestedRoom) || requestedRoom is null)
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
                await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Rate limit exceeded", context.RequestAborted);
                break;
            }

            await BroadcastAsync(rooms, room, connectionId, message, options.SendTimeout, app.Logger);
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
    return Results.Ok(new
    {
        status = "ok",
        rooms = stats.Rooms,
        connections = stats.Connections,
        options.MaxPeersPerRoom,
        options.MaxMessageBytes,
        options.MaxMessagesPerMinute
    });
});

app.MapGet("/metrics", () =>
{
    var stats = rooms.Stats;
    var text = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"mmn_sync_rooms {stats.Rooms}\nmmn_sync_connections {stats.Connections}\n");
    return Results.Text(text, "text/plain; version=0.0.4");
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

static async Task BroadcastAsync(
    SyncRoomRegistry<WebSocket> rooms,
    string room,
    Guid senderId,
    string message,
    TimeSpan sendTimeout,
    ILogger logger)
{
    var payload = Encoding.UTF8.GetBytes(message);
    foreach (var peer in rooms.GetPeers(room))
    {
        if (peer.Key == senderId)
        {
            continue;
        }

        if (peer.Value.State is not WebSocketState.Open)
        {
            rooms.Leave(room, peer.Key);
            continue;
        }

        using var timeout = new CancellationTokenSource(sendTimeout);
        try
        {
            await peer.Value.SendAsync(payload, WebSocketMessageType.Text, true, timeout.Token);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            SyncLog.RemovingUnavailablePeer(logger, exception, room);
            rooms.Leave(room, peer.Key);
        }
    }
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

sealed record JoinMessage(string Room);
