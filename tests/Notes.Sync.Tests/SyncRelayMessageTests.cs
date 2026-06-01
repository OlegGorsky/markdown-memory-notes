using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRelayMessageTests
{
    [Theory]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","messageId":"0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","baseHash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"delete","path":"inbox/2026-05-31.md","content":null}""")]
    [InlineData("""{"type":"delete","path":"inbox/2026-05-31.md","baseHash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.md","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}],"truncated":false,"messageId":"0123456789abcdef0123456789abcdef"}""")]
    public void IsValidAcceptsSupportedMessages(string json)
    {
        Assert.True(SyncRelayMessage.IsValid(json, maxContentBytes: 1024));
    }

    [Fact]
    public void IsValidAcceptsLegacyPascalCaseClientMessages()
    {
        var json = """{"Type":"file","Path":"notes/a.md","Content":"# A"}""";

        Assert.True(SyncRelayMessage.IsValid(json, maxContentBytes: 1024));
    }

    [Theory]
    [InlineData("""{"type":"file","path":"../secrets.md","content":"oops"}""")]
    [InlineData("""{"type":"file","path":".notes/trails.json","content":"{}"}""")]
    [InlineData("""{"type":"delete","path":"notes/a.txt","content":null}""")]
    [InlineData("""{"type":"rename","path":"notes/a.md","content":null}""")]
    [InlineData("""{"type":"presence","peerCount":2}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":null}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","baseHash":"bad hash"}""")]
    [InlineData("""{"type":"delete","path":"notes/a.md","baseHash":"bad hash"}""")]
    [InlineData("""{"type":"delete","path":"notes/a.md","content":"unneeded payload"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","messageId":"bad"}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.md","hash":"bad"}]}""")]
    [InlineData("""{"type":"ack","messageId":"0123456789abcdef0123456789abcdef"}""")]
    public void IsValidRejectsUnsafeMessages(string json)
    {
        Assert.False(SyncRelayMessage.IsValid(json, maxContentBytes: 1024));
    }

    [Fact]
    public void TryGetMessageIdReadsValidMessageId()
    {
        var json = """{"type":"file","path":"notes/a.md","content":"# A","messageId":"0123456789abcdef0123456789abcdef"}""";

        Assert.True(SyncRelayMessage.TryGetMessageId(json, out var messageId));
        Assert.Equal("0123456789abcdef0123456789abcdef", messageId);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData(""""not-an-object"""")]
    public void TryGetMessageIdRejectsNonObjectPayloads(string json)
    {
        Assert.False(SyncRelayMessage.TryGetMessageId(json, out _));
    }

    [Fact]
    public void IsValidRejectsOversizedContent()
    {
        var json = """{"type":"file","path":"notes/a.md","content":"too long"}""";

        Assert.False(SyncRelayMessage.IsValid(json, maxContentBytes: 4));
    }

    [Theory]
    [InlineData("""{"type":"file","Type":"delete","path":"notes/a.md","content":"# A"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","Path":"notes/b.md","content":"# A"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","Content":"# B"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","baseHash":null,"BaseHash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A","messageId":null,"MessageId":"0123456789abcdef0123456789abcdef"}""")]
    public void IsValidRejectsDuplicateProtocolProperties(string json)
    {
        Assert.False(SyncRelayMessage.IsValid(json, maxContentBytes: 1024));
    }
}
