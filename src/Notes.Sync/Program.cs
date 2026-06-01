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
var connectionAttempts = new SyncConnectionAttemptLimiter(
    options.MaxConnectionAttemptsPerMinute,
    TimeSpan.FromMinutes(1));
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
    options.SendTimeout,
    metrics,
    app.Logger);
var backplaneBridge = new SyncBackplaneBridge<WebSocket>(
    options.InstanceId,
    rooms,
    broadcaster,
    backplane,
    options.MaxMessageBytes,
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
broadcaster.SetPeerRemovedHandler(CleanupRemovedPeerAsync);

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

    var clientKey = ClientConnectionKey(context);
    if (!connectionAttempts.TryConsume(clientKey))
    {
        metrics.ConnectionRateLimited();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsync("Connection rate limit exceeded", context.RequestAborted);
        return;
    }

    using var connectionLease = connections.TryAcquire(clientKey);
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
            token => SyncWebSocketMessageReceiver.ReceiveTextAsync(
                ws,
                options.MaxMessageBytes,
                options.JoinTimeout,
                token),
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
            var message = await SyncWebSocketMessageReceiver.ReceiveTextAsync(
                ws,
                options.MaxMessageBytes,
                options.ReceiveTimeout,
                context.RequestAborted);
            if (message is null)
            {
                break;
            }

            if (!rooms.Contains(room, connectionId))
            {
                await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Peer removed", context.RequestAborted);
                break;
            }

            if (!rateLimit.TryConsume())
            {
                metrics.MessageRateLimited();
                await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Rate limit exceeded", context.RequestAborted);
                break;
            }

            if (!SyncRelayMessage.TryClassify(message, options.MaxMessageBytes, out var classification))
            {
                metrics.MessageRejected();
                await CloseSafeAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Invalid sync message", context.RequestAborted);
                break;
            }

            if (classification.Kind is SyncRelayMessageKind.Heartbeat)
            {
                continue;
            }

            metrics.MessageReceived();
            var result = await broadcaster.BroadcastAsync(room, connectionId, message, options.SendTimeout, app.Logger);
            var backplaneResult = await backplaneBridge.PublishAsync(room, connectionId, message, context.RequestAborted);
            if ((result.Succeeded > 0 || backplaneResult.RemoteSubscribers > 0) &&
                classification.MessageId is not null)
            {
                var ackSent = await broadcaster.SendToPeerAsync(
                    room,
                    connectionId,
                    ws,
                    SyncAckMessage.Create(classification.MessageId),
                    options.SendTimeout,
                    app.Logger);
                if (!ackSent)
                {
                    break;
                }
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
    catch (TimeoutException exception)
    {
        SyncLog.ProtocolViolation(app.Logger, exception);
        if (room is null)
        {
            metrics.JoinTimedOut();
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Join timeout", CancellationToken.None);
        }
        else
        {
            metrics.ReceiveTimedOut();
            await CloseSafeAsync(ws, WebSocketCloseStatus.PolicyViolation, "Receive timeout", CancellationToken.None);
        }
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

async Task CleanupRemovedPeerAsync(string removedRoom, Guid removedConnectionId, CancellationToken cancellationToken)
{
    using var timeout = new CancellationTokenSource(options.SendTimeout);
    using var cleanupToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
    await admissionCoordinator.PeerLeftAsync(removedRoom, removedConnectionId, cleanupToken.Token);
    await presenceCoordinator.PeerLeftAsync(removedRoom, removedConnectionId, cleanupToken.Token);
    await backplaneBridge.ReleaseIfRoomEmptyAsync(removedRoom);
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
        activeSendGates = broadcaster.SendGateCount,
        counters,
        options.MaxConnections,
        options.MaxConnectionsPerClient,
        options.MaxConnectionAttemptsPerMinute,
        options.MaxPeersPerRoom,
        options.MaxMessageBytes,
        options.MaxMessagesPerMinute,
        options.MaxFanoutConcurrency,
        options.MaxBackplaneReceiveQueue,
        backplaneEnabled = backplane.IsEnabled,
        backplaneHealthy = backplaneHealth.Healthy,
        backplaneHealth,
        distributedPresenceEnabled = presenceCoordinator.IsDistributed,
        distributedAdmissionEnabled = admissionCoordinator.IsDistributed,
        activeBackplaneSubscriptions = backplaneBridge.SubscriptionCount,
        activeBackplaneReceiveGates = backplaneBridge.ReceiveGateCount,
        options.BackplaneChannelPrefix,
        options.InstanceId,
        joinTimeoutSeconds = options.JoinTimeout.TotalSeconds,
        receiveTimeoutSeconds = options.ReceiveTimeout.TotalSeconds,
        trustedProxiesConfigured = options.TrustedProxies.Count,
        trustedNetworksConfigured = options.TrustedNetworks.Count,
        allowedOriginsConfigured = options.AllowedOrigins.Count
    });
});

app.MapGet("/metrics", () =>
{
    return Results.Text(
        metrics.RenderPrometheus(
            rooms.Stats,
            connections.ActiveConnections,
            backplaneBridge.SubscriptionCount,
            broadcaster.SendGateCount,
            backplaneBridge.ReceiveGateCount),
        "text/plain; version=0.0.4");
});

await app.RunAsync(Environment.GetEnvironmentVariable("MMN_SYNC_URL") ?? "http://0.0.0.0:5199");

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
