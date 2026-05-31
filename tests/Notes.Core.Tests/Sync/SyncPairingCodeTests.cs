using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncPairingCodeTests
{
    [Fact]
    public void GenerateCreatesHighEntropyUrlSafeCode()
    {
        var code = SyncPairingCode.Generate();

        Assert.InRange(code.Length, 22, 64);
        Assert.Matches("^[A-Za-z0-9_-]+$", code);
    }

    [Fact]
    public void GenerateDoesNotRepeatAcrossManyCodes()
    {
        var codes = Enumerable.Range(0, 256)
            .Select(_ => SyncPairingCode.Generate())
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }
}
