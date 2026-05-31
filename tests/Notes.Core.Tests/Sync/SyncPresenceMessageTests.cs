using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncPresenceMessageTests
{
    [Fact]
    public void CreateSerializesCamelCasePresenceMessage()
    {
        var json = SyncPresenceMessage.Create(peerCount: 2);

        Assert.Contains("\"type\":\"presence\"", json, StringComparison.Ordinal);
        Assert.Contains("\"peerCount\":2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseReadsValidPresenceMessage()
    {
        Assert.True(SyncPresenceMessage.TryParse("""{"type":"presence","peerCount":3}""", out var peerCount));
        Assert.Equal(3, peerCount);
    }

    [Theory]
    [InlineData("""{"type":"file","peerCount":3}""")]
    [InlineData("""{"type":"presence","peerCount":0}""")]
    [InlineData("""{"type":"presence","peerCount":-1}""")]
    [InlineData("""{"type":"presence","peerCount":"2"}""")]
    [InlineData("""not-json""")]
    public void TryParseRejectsInvalidPresenceMessages(string json)
    {
        Assert.False(SyncPresenceMessage.TryParse(json, out _));
    }
}
