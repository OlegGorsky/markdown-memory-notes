namespace Notes.Sync;

public sealed class SyncConnectionAttemptLimiter
{
    private readonly int limit;
    private readonly TimeSpan window;
    private readonly Func<DateTimeOffset> now;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> attemptsByKey = new(StringComparer.Ordinal);
    private DateTimeOffset nextPruneAt;

    public SyncConnectionAttemptLimiter(int limit, TimeSpan window, Func<DateTimeOffset>? now = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        this.limit = limit;
        this.window = window;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        nextPruneAt = DateTimeOffset.MinValue;
    }

    public int TrackedClientCount
    {
        get
        {
            lock (gate)
            {
                return attemptsByKey.Count;
            }
        }
    }

    public bool TryConsume(string key)
    {
        var normalizedKey = SyncConnectionLimiter.NormalizeKey(key);
        lock (gate)
        {
            var current = now();
            if (current >= nextPruneAt)
            {
                PruneExpiredNoLock(current);
                nextPruneAt = current + window;
            }

            if (!attemptsByKey.TryGetValue(normalizedKey, out var attempts))
            {
                attempts = new Queue<DateTimeOffset>();
                attemptsByKey[normalizedKey] = attempts;
            }

            PruneExpiredNoLock(attempts, current);
            if (attempts.Count >= limit)
            {
                return false;
            }

            attempts.Enqueue(current);
            return true;
        }
    }

    private void PruneExpiredNoLock(DateTimeOffset current)
    {
        var emptyKeys = new List<string>();
        foreach (var pair in attemptsByKey)
        {
            PruneExpiredNoLock(pair.Value, current);
            if (pair.Value.Count == 0)
            {
                emptyKeys.Add(pair.Key);
            }
        }

        foreach (var key in emptyKeys)
        {
            attemptsByKey.Remove(key);
        }
    }

    private void PruneExpiredNoLock(Queue<DateTimeOffset> attempts, DateTimeOffset current)
    {
        while (attempts.Count > 0 && current - attempts.Peek() >= window)
        {
            attempts.Dequeue();
        }
    }
}
