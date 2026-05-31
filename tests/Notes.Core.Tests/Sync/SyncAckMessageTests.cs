using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncAckMessageTests
{
    [Fact]
    public void CreateSerializesCamelCaseAckMessage()
    {
        var messageId = "0123456789abcdef0123456789abcdef";

        var json = SyncAckMessage.Create(messageId);

        Assert.Contains("\"type\":\"ack\"", json, StringComparison.Ordinal);
        Assert.Contains("\"messageId\":\"0123456789abcdef0123456789abcdef\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseReadsValidAckMessage()
    {
        Assert.True(SyncAckMessage.TryParse(
            """{"type":"ack","messageId":"0123456789abcdef0123456789abcdef"}""",
            out var messageId));
        Assert.Equal("0123456789abcdef0123456789abcdef", messageId);
    }

    [Theory]
    [InlineData("""{"type":"file","messageId":"0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"ack","messageId":"bad"}""")]
    [InlineData("""{"type":"ack","messageId":123}""")]
    [InlineData("""not-json""")]
    public void TryParseRejectsInvalidAckMessages(string json)
    {
        Assert.False(SyncAckMessage.TryParse(json, out _));
    }
}
