using Microsoft.Extensions.Configuration;

namespace Notes.Sync;

public sealed record SyncServerOptions(
    int MaxRooms,
    int MaxPeersPerRoom,
    int MaxMessageBytes,
    int MaxMessagesPerMinute,
    int MaxConnections,
    int MaxConnectionsPerClient,
    int MaxFanoutConcurrency,
    TimeSpan SendTimeout,
    IReadOnlyList<string> AllowedOrigins)
{
    public static SyncServerOptions Default { get; } = new(
        MaxRooms: 10_000,
        MaxPeersPerRoom: 32,
        MaxMessageBytes: 64 * 1024,
        MaxMessagesPerMinute: 120,
        MaxConnections: 20_000,
        MaxConnectionsPerClient: 256,
        MaxFanoutConcurrency: 16,
        SendTimeout: TimeSpan.FromSeconds(5),
        AllowedOrigins: []);

    public static SyncServerOptions FromConfiguration(IConfiguration configuration)
    {
        var maxPeersPerRoom = GetInt(configuration, "MMN_SYNC_MAX_PEERS_PER_ROOM", "Sync:MaxPeersPerRoom", Default.MaxPeersPerRoom, 1, 512);
        var maxConnections = GetInt(configuration, "MMN_SYNC_MAX_CONNECTIONS", "Sync:MaxConnections", Default.MaxConnections, 1, 1_000_000);
        return new SyncServerOptions(
            GetInt(configuration, "MMN_SYNC_MAX_ROOMS", "Sync:MaxRooms", Default.MaxRooms, 1, 100_000),
            maxPeersPerRoom,
            GetInt(configuration, "MMN_SYNC_MAX_MESSAGE_BYTES", "Sync:MaxMessageBytes", Default.MaxMessageBytes, 1024, 4 * 1024 * 1024),
            GetInt(configuration, "MMN_SYNC_MAX_MESSAGES_PER_MINUTE", "Sync:MaxMessagesPerMinute", Default.MaxMessagesPerMinute, 1, 10_000),
            maxConnections,
            GetInt(configuration, "MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT", "Sync:MaxConnectionsPerClient", Default.MaxConnectionsPerClient, 1, maxConnections),
            GetInt(configuration, "MMN_SYNC_MAX_FANOUT_CONCURRENCY", "Sync:MaxFanoutConcurrency", Default.MaxFanoutConcurrency, 1, maxPeersPerRoom),
            TimeSpan.FromSeconds(GetInt(configuration, "MMN_SYNC_SEND_TIMEOUT_SECONDS", "Sync:SendTimeoutSeconds", (int)Default.SendTimeout.TotalSeconds, 1, 60)),
            GetAllowedOrigins(configuration));
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

    private static string[] GetAllowedOrigins(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("MMN_SYNC_ALLOWED_ORIGINS") ?? configuration["Sync:AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(origin => SyncOriginPolicy.NormalizeConfiguredOrigin(origin) ?? origin)
            .ToArray();
    }
}
