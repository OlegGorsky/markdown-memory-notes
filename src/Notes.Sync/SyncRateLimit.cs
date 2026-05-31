namespace Notes.Sync;

public sealed class SyncRateLimit
{
    private readonly int limit;
    private readonly TimeSpan window;
    private readonly Func<DateTimeOffset> now;
    private readonly Lock gate = new();
    private DateTimeOffset windowStart;
    private int count;

    public SyncRateLimit(int limit, TimeSpan window, Func<DateTimeOffset>? now = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        this.limit = limit;
        this.window = window;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        windowStart = this.now();
    }

    public bool TryConsume()
    {
        lock (gate)
        {
            var current = now();
            if (current - windowStart >= window)
            {
                windowStart = current;
                count = 0;
            }

            if (count >= limit)
            {
                return false;
            }

            count++;
            return true;
        }
    }
}
