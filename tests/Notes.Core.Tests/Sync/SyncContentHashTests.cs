using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncContentHashTests
{
    [Fact]
    public void ComputeCreatesStableUrlSafeHash()
    {
        var first = SyncContentHash.Compute("# Note\n");
        var second = SyncContentHash.Compute("# Note\n");

        Assert.Equal(first, second);
        Assert.True(SyncContentHash.IsValid(first));
        Assert.Matches("^[A-Za-z0-9_-]+$", first);
    }

    [Fact]
    public void IsValidRejectsUnsafeHashes()
    {
        Assert.False(SyncContentHash.IsValid(null));
        Assert.False(SyncContentHash.IsValid(""));
        Assert.False(SyncContentHash.IsValid("bad hash"));
        Assert.False(SyncContentHash.IsValid("../secret"));
    }
}
