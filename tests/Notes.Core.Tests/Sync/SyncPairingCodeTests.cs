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
        Assert.True(SyncPairingCode.IsValid(code));
    }

    [Fact]
    public void GenerateDoesNotRepeatAcrossManyCodes()
    {
        var codes = Enumerable.Range(0, 256)
            .Select(_ => SyncPairingCode.Generate())
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("AbCdEfGhIjKlMnOpQrStUv")]
    [InlineData("room-2026_ABCDEFGHijkl")]
    public void IsValidAcceptsHighEntropyCodes(string code)
    {
        Assert.True(SyncPairingCode.IsValid(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD1234")]
    [InlineData("team_notes")]
    [InlineData("room with space")]
    [InlineData("../secret")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    public void IsValidRejectsWeakOrUnsafeCodes(string code)
    {
        Assert.False(SyncPairingCode.IsValid(code));
    }
}
