using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncRepairRequestMessageTests
{
    [Fact]
    public void CreateSerializesBoundedCamelCaseRepairRequest()
    {
        var json = SyncRepairRequestMessage.Create(
            [new SyncManifestEntry("notes/a.md", SyncContentHash.Compute("# A"))],
            truncated: true,
            messageId: "0123456789abcdef0123456789abcdef");

        Assert.Contains("\"type\":\"repairRequest\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/a.md\"", json, StringComparison.Ordinal);
        Assert.Contains("\"truncated\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"messageId\":\"0123456789abcdef0123456789abcdef\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseReadsValidRepairRequest()
    {
        var hash = SyncContentHash.Compute("# A");
        var json = $$"""{"type":"repairRequest","entries":[{"path":"notes/a.md","hash":"{{hash}}"}],"truncated":false,"messageId":"0123456789abcdef0123456789abcdef"}""";

        Assert.True(SyncRepairRequestMessage.TryParse(json, out var request));
        var entry = Assert.Single(request.Entries);
        Assert.Equal("notes/a.md", entry.Path);
        Assert.Equal(hash, entry.Hash);
        Assert.False(request.Truncated);
    }

    [Theory]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"../secret.md","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.txt","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.md","hash":"bad"}]}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.md","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},{"path":"notes/a.md","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}""")]
    [InlineData("""{"type":"repairRequest","entries":[{"path":"notes/a.md","Path":"notes/b.md","hash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}""")]
    [InlineData("""{"type":"repairRequest","entries":[],"content":"# no"}""")]
    [InlineData("""{"type":"repairRequest","entries":[],"Content":"# no"}""")]
    [InlineData("""{"type":"repairRequest","entries":[],"messageId":"bad"}""")]
    public void TryParseRejectsInvalidRepairRequests(string json)
    {
        Assert.False(SyncRepairRequestMessage.TryParse(json, out _));
    }
}
