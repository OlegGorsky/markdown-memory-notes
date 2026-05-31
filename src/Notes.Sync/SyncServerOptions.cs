using Microsoft.Extensions.Configuration;

namespace Notes.Sync;

public sealed record SyncServerOptions(
    int MaxRooms,
    int MaxPeersPerRoom,
    int MaxMessageBytes,
    int MaxMessagesPerMinute,
    TimeSpan SendTimeout)
{
    public static SyncServerOptions Default { get; } = new(
        MaxRooms: 10_000,
        MaxPeersPerRoom: 32,
        MaxMessageBytes: 64 * 1024,
        MaxMessagesPerMinute: 120,
        SendTimeout: TimeSpan.FromSeconds(5));

    public static SyncServerOptions FromConfiguration(IConfiguration configuration)
    {
        return new SyncServerOptions(
            GetInt(configuration, "MMN_SYNC_MAX_ROOMS", "Sync:MaxRooms", Default.MaxRooms, 1, 100_000),
            GetInt(configuration, "MMN_SYNC_MAX_PEERS_PER_ROOM", "Sync:MaxPeersPerRoom", Default.MaxPeersPerRoom, 1, 512),
            GetInt(configuration, "MMN_SYNC_MAX_MESSAGE_BYTES", "Sync:MaxMessageBytes", Default.MaxMessageBytes, 1024, 4 * 1024 * 1024),
            GetInt(configuration, "MMN_SYNC_MAX_MESSAGES_PER_MINUTE", "Sync:MaxMessagesPerMinute", Default.MaxMessagesPerMinute, 1, 10_000),
            TimeSpan.FromSeconds(GetInt(configuration, "MMN_SYNC_SEND_TIMEOUT_SECONDS", "Sync:SendTimeoutSeconds", (int)Default.SendTimeout.TotalSeconds, 1, 60)));
    }

    private static int GetInt(IConfiguration configuration, string environmentKey, string configKey, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(environmentKey) ?? configuration[configKey];
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
