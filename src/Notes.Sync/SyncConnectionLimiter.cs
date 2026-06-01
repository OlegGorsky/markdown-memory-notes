using System.Net;

namespace Notes.Sync;

public sealed class SyncConnectionLimiter
{
    private readonly Dictionary<string, int> connectionsByKey = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private int activeConnections;

    public SyncConnectionLimiter(int maxConnections, int maxConnectionsPerKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConnections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConnectionsPerKey);

        MaxConnections = maxConnections;
        MaxConnectionsPerKey = maxConnectionsPerKey;
    }

    public int MaxConnections { get; }
    public int MaxConnectionsPerKey { get; }

    public int ActiveConnections
    {
        get
        {
            lock (gate)
            {
                return activeConnections;
            }
        }
    }

    public SyncConnectionLease TryAcquire(string key)
    {
        var normalizedKey = NormalizeKey(key);
        lock (gate)
        {
            if (activeConnections >= MaxConnections)
            {
                return SyncConnectionLease.Rejected;
            }

            connectionsByKey.TryGetValue(normalizedKey, out var keyConnections);
            if (keyConnections >= MaxConnectionsPerKey)
            {
                return SyncConnectionLease.Rejected;
            }

            connectionsByKey[normalizedKey] = keyConnections + 1;
            activeConnections++;
            return new SyncConnectionLease(this, normalizedKey);
        }
    }

    private void Release(string key)
    {
        lock (gate)
        {
            if (!connectionsByKey.TryGetValue(key, out var keyConnections))
            {
                return;
            }

            activeConnections = Math.Max(0, activeConnections - 1);
            if (keyConnections <= 1)
            {
                connectionsByKey.Remove(key);
            }
            else
            {
                connectionsByKey[key] = keyConnections - 1;
            }
        }
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "unknown";
        }

        var trimmed = key.Trim();
        if (!IPAddress.TryParse(trimmed, out var address))
        {
            return trimmed;
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }

    internal void ReleaseLease(string key)
    {
        Release(key);
    }
}

public sealed class SyncConnectionLease : IDisposable
{
    internal static readonly SyncConnectionLease Rejected = new(null, string.Empty);
    private readonly SyncConnectionLimiter? owner;
    private readonly string key;
    private int disposed;

    internal SyncConnectionLease(SyncConnectionLimiter? owner, string key)
    {
        this.owner = owner;
        this.key = key;
        Acquired = owner is not null;
    }

    public bool Acquired { get; }

    public void Dispose()
    {
        if (!Acquired || Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        owner?.ReleaseLease(key);
    }
}
