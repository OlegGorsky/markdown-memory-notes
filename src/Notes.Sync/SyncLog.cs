using Microsoft.Extensions.Logging;

namespace Notes.Sync;

public static partial class SyncLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Sync peer connected. Room={Room} Connections={Connections}")]
    public static partial void PeerConnected(ILogger logger, string room, int connections);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Sync peer disconnected. Room={Room} Connections={Connections}")]
    public static partial void PeerDisconnected(ILogger logger, string room, int connections);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Sync protocol violation")]
    public static partial void ProtocolViolation(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Invalid sync JSON payload")]
    public static partial void InvalidJsonPayload(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Sync socket closed unexpectedly")]
    public static partial void SocketClosedUnexpectedly(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Sync socket request was cancelled")]
    public static partial void RequestCancelled(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Removing unavailable sync peer. Room={Room}")]
    public static partial void RemovingUnavailablePeer(ILogger logger, Exception exception, string room);
}
