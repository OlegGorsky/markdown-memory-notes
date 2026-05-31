using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Notes.Core.Sync;
using Notes.Sync;
#pragma warning disable CA1812 // Instantiated via JSON deserialization

var builder = WebApplication.CreateBuilder(args);
var options = SyncServerOptions.FromConfiguration(builder.Configuration);
var app = builder.Build();
var rooms = new SyncRoomRegistry<WebSocket>(options.MaxRooms, options.MaxPeersPerRoom);
var metrics = new SyncMetrics();
var connections = new SyncConnectionLimiter(options.MaxConnections, options.MaxConnectionsPerClient);
var broadcaster = new SyncBroadcaster<WebSocket>(
    rooms,
    static socket => socket.State is WebSocketState.Open,
    SendSocketAsync,
    options.MaxFanoutConcurrency,
    metrics);
await using var backplane = await SyncBackplaneFactory.CreateAsync(options, metrics, app.Logger);
var admissionCoordinator = new SyncAdmissionCoordinator<WebSocket>(
    rooms,
    backplane as ISyncAdmissionController ?? NoopSyncAdmissionController.Instance,
    options.MaxRooms,
    options.MaxPeersPerRoom,
    metrics,
    app.Logger);
var backplaneBridge = new SyncBackplaneBridge<WebSocket>(
    options.InstanceId,
    rooms,
    broadcaster,
    backplane,
    options.SendTimeout,
    metrics,
    app.Logger);
using var presenceCoordinator = new SyncPresenceCoordinator<WebSocket>(
    rooms,
    broadcaster,
    backplaneBridge,
    backplane as ISyncPresenceTracker ?? NoopSyncPresenceTracker.Instance,
    options.SendTimeout,
    metrics,
    app.Logger);

if (SyncForwardedHeadersPolicy.IsConfigured(options))
{
    app.UseForwardedHeaders(SyncForwardedHeadersPolicy.Create(options));
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.Map("/sync", HandleSyncRequestAsync);

async Task HandleSyncRequestAsync(HttpContext context)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required", context.RequestAborted);
        return;
    }

    var origin = context.Request.Headers.TryGetValue("Origin", out var originHeader)
        ? originHeader.ToString()
        : null;
    if (!SyncOriginPolicy.IsAllowed(origin, options.AllowedOrigins))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Origin not allowed", context.RequestAborted);
        return;
    }

    using var connectionLease = connections.TryAcquire(ClientConnectionKey(context));
    if (!connectionLease.Acquired)
    {
        metrics.ConnectionLimitRejected();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsync("Connection limit exceeded", context.RequestAborted);
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid();
    string? room = null;
    var joined = false;

    try
    {
        var join = await SyncJoinPayloadReceiver.ReceiveAsync(
            token => ReceiveTextAsync(ws, options.MaxMessageBytes, token),
            options.JoinTimeout,
            context.RequestAborted);
        if (join.Status is SyncJoinPayloadStatus.Closed)
        {
            return;
        }

        if (join.Status is SyncJoinPayloadStatus.TimedOut)
        {
            metrics.JoinTimedOut();
            ws.Abort();
            return;
        }

        var joinPayload = join.Payload ?? string.Empty;
        if (!SyncJoinRequest.TryGetRoom(joinPayload, out var requestedRoom))
        {
            metrics.JoinRejected();
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Invalid room", context.RequestAborted);
            return;
        }

        room = requestedRoom;
        var joinResult = await admissionCoordinator.TryJoinAsync(room, connectionId, ws, context.RequestAborted);
        if (joinResult is not SyncJoinResult.Joined)
        {
            metrics.JoinRejected();
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, JoinResultMessage(joinResult), context.RequestAborted);
            return;
        }

        joined = true;
        if (app.Logger.IsEnabled(LogLevel.Information))
        {
            SyncLog.PeerConnected(app.Logger, room, rooms.Stats.Connections);
        }

        await backplaneBridge.EnsureSubscribedAsync(room, context.RequestAborted);
        await presenceCoordinator.PeerJoinedAsync(room, connectionId, context.RequestAborted);
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
            var result = await broadcaster.BroadcastAsync(room, connectionId, message, options.SendTimeout, app.Logger);
            var backplaneResult = await backplaneBridge.PublishAsync(room, connectionId, message, context.RequestAborted);
            if ((result.Succeeded > 0 || backplaneResult.RemoteSubscribers > 0) &&
                SyncRelayMessage.TryGetMessageId(message, out var messageId))
            {
                await SendSocketAsync(ws, SyncAckMessage.Create(messageId), context.RequestAborted);
            }

            if (result.Failed > 0 || result.Attempted == 0)
            {
                await presenceCoordinator.BroadcastAsync(room, context.RequestAborted);
            }
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
        if (room is null && !context.RequestAborted.IsCancellationRequested)
        {
            metrics.JoinTimedOut();
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Join timeout", CancellationToken.None);
        }
        else
        {
            SyncLog.RequestCancelled(app.Logger);
        }
    }
    finally
    {
        if (joined && room is not null)
        {
            await admissionCoordinator.PeerLeftAsync(room, connectionId, CancellationToken.None);
            broadcaster.ForgetPeer(connectionId);
            await presenceCoordinator.PeerLeftAsync(room, connectionId, CancellationToken.None);
            await backplaneBridge.ReleaseIfRoomEmptyAsync(room);
            if (app.Logger.IsEnabled(LogLevel.Information))
            {
                SyncLog.PeerDisconnected(app.Logger, room, rooms.Stats.Connections);
            }
        }
    }
}

app.MapGet("/health", async (CancellationToken cancellationToken) =>
{
    var stats = rooms.Stats;
    var counters = metrics.Snapshot();
    var backplaneHealth = await backplane.CheckHealthAsync(cancellationToken);
    return Results.Ok(new
    {
        status = backplaneHealth.Healthy ? "ok" : "degraded",
        rooms = stats.Rooms,
        connections = stats.Connections,
        activeWebSockets = connections.ActiveConnections,
        counters,
        options.MaxConnections,
        options.MaxConnectionsPerClient,
        options.MaxPeersPerRoom,
        options.MaxMessageBytes,
        options.MaxMessagesPerMinute,
        options.MaxFanoutConcurrency,
        backplaneEnabled = backplane.IsEnabled,
        backplaneHealthy = backplaneHealth.Healthy,
        backplaneHealth,
        distributedPresenceEnabled = presenceCoordinator.IsDistributed,
        distributedAdmissionEnabled = admissionCoordinator.IsDistributed,
        activeBackplaneSubscriptions = backplaneBridge.SubscriptionCount,
        options.BackplaneChannelPrefix,
        options.InstanceId,
        joinTimeoutSeconds = options.JoinTimeout.TotalSeconds,
        trustedProxiesConfigured = options.TrustedProxies.Count,
        trustedNetworksConfigured = options.TrustedNetworks.Count,
        allowedOriginsConfigured = options.AllowedOrigins.Count
    });
});

app.MapGet("/metrics", () =>
{
    return Results.Text(
        metrics.RenderPrometheus(rooms.Stats, connections.ActiveConnections, backplaneBridge.SubscriptionCount),
        "text/plain; version=0.0.4");
});

await app.RunAsync(Environment.GetEnvironmentVariable("MMN_SYNC_URL") ?? "http://0.0.0.0:5199");

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

static string ClientConnectionKey(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
