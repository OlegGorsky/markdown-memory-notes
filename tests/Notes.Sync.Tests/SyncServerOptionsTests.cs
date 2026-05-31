using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncServerOptionsTests
{
    [Fact]
    public void DefaultsAreBoundedForPublicRelay()
    {
        var options = SyncServerOptions.Default;

        Assert.InRange(options.MaxRooms, 1, 20_000);
        Assert.InRange(options.MaxPeersPerRoom, 1, 128);
        Assert.InRange(options.MaxMessageBytes, 1, 1024 * 1024);
        Assert.InRange(options.MaxMessagesPerMinute, 1, 1_000);
        Assert.InRange(options.MaxFanoutConcurrency, 1, options.MaxPeersPerRoom);
    }
}
