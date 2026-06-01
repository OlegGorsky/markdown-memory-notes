using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncBackplaneHealthCacheTests
{
    [Fact]
    public async Task GetAsyncReusesCachedHealthWithinTtl()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var checks = 0;
        using var cache = new SyncBackplaneHealthCache(TimeSpan.FromSeconds(1), () => now);

        var first = await cache.GetAsync(CheckAsync, CancellationToken.None);
        now = now.AddMilliseconds(500);
        var second = await cache.GetAsync(CheckAsync, CancellationToken.None);

        Assert.Equal(1, checks);
        Assert.Equal(first, second);

        Task<SyncBackplaneHealth> CheckAsync(CancellationToken _)
        {
            checks++;
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(checks)));
        }
    }

    [Fact]
    public async Task GetAsyncRefreshesCachedHealthAfterTtl()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var checks = 0;
        using var cache = new SyncBackplaneHealthCache(TimeSpan.FromSeconds(1), () => now);

        var first = await cache.GetAsync(CheckAsync, CancellationToken.None);
        now = now.AddSeconds(2);
        var second = await cache.GetAsync(CheckAsync, CancellationToken.None);

        Assert.Equal(2, checks);
        Assert.NotEqual(first, second);

        Task<SyncBackplaneHealth> CheckAsync(CancellationToken _)
        {
            checks++;
            return Task.FromResult(SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(checks)));
        }
    }

    [Fact]
    public async Task GetAsyncCoalescesConcurrentRefreshes()
    {
        var checks = 0;
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new SyncBackplaneHealthCache(TimeSpan.FromSeconds(1));

        var first = cache.GetAsync(CheckAsync, CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var second = cache.GetAsync(CheckAsync, CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, checks);

        releaseRefresh.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, checks);

        async Task<SyncBackplaneHealth> CheckAsync(CancellationToken _)
        {
            checks++;
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            return SyncBackplaneHealth.Available(TimeSpan.FromMilliseconds(1));
        }
    }
}
