using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRelayMessageTests
{
    [Theory]
    [InlineData("""{"type":"file","path":"notes/a.md","content":"# A"}""")]
    [InlineData("""{"type":"delete","path":"inbox/2026-05-31.md","content":null}""")]
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
    [InlineData("""{"type":"file","path":"notes/a.md","content":null}""")]
    public void IsValidRejectsUnsafeMessages(string json)
    {
        Assert.False(SyncRelayMessage.IsValid(json, maxContentBytes: 1024));
    }

    [Fact]
    public void IsValidRejectsOversizedContent()
    {
        var json = """{"type":"file","path":"notes/a.md","content":"too long"}""";

        Assert.False(SyncRelayMessage.IsValid(json, maxContentBytes: 4));
    }
}
