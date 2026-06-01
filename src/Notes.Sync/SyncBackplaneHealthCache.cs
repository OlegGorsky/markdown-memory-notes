namespace Notes.Sync;

public sealed class SyncBackplaneHealthCache : IDisposable
{
    private readonly TimeSpan ttl;
    private readonly Func<DateTimeOffset> now;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private SyncBackplaneHealth? cached;
    private DateTimeOffset expiresAt;
    private int disposed;

    public SyncBackplaneHealthCache(TimeSpan ttl, Func<DateTimeOffset>? now = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        this.ttl = ttl;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<SyncBackplaneHealth> GetAsync(
        Func<CancellationToken, Task<SyncBackplaneHealth>> checkAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkAsync);
        ThrowIfDisposed();

        if (TryGetCached(out var health))
        {
            return health;
        }

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (TryGetCached(out health))
            {
                return health;
            }

            health = await checkAsync(cancellationToken);
            lock (gate)
            {
                cached = health;
                expiresAt = now() + ttl;
            }

            return health;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        refreshGate.Dispose();
    }

    private bool TryGetCached(out SyncBackplaneHealth health)
    {
        lock (gate)
        {
            if (cached is not null && now() < expiresAt)
            {
                health = cached;
                return true;
            }
        }

        health = SyncBackplaneHealth.Unavailable;
        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
    }
}
