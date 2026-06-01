namespace Notes.Sync;

public sealed class SyncRateLimit
{
    private readonly int limit;
    private readonly TimeSpan window;
    private readonly Func<DateTimeOffset> now;
    private readonly Lock gate = new();
    private readonly Queue<DateTimeOffset> acceptedAt = new();

    public SyncRateLimit(int limit, TimeSpan window, Func<DateTimeOffset>? now = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        this.limit = limit;
        this.window = window;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryConsume()
    {
        lock (gate)
        {
            var current = now();
            while (acceptedAt.Count > 0 && current - acceptedAt.Peek() >= window)
            {
                acceptedAt.Dequeue();
            }

            if (acceptedAt.Count >= limit)
            {
                return false;
            }

            acceptedAt.Enqueue(current);
            return true;
        }
    }
}
