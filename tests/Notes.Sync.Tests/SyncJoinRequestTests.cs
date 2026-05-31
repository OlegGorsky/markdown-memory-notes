using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncJoinRequestTests
{
    [Theory]
    [InlineData("""{"room":"AbCdEfGhIjKlMnOpQrStUv"}""", "AbCdEfGhIjKlMnOpQrStUv")]
    [InlineData("""{"Room":"legacy-PascalCase-1234"}""", "legacy-PascalCase-1234")]
    public void TryGetRoomAcceptsSupportedJsonCasing(string json, string expectedRoom)
    {
        Assert.True(SyncJoinRequest.TryGetRoom(json, out var room));
        Assert.Equal(expectedRoom, room);
    }

    [Theory]
    [InlineData("""{"room":"bad room"}""")]
    [InlineData("""{"room":"abc"}""")]
    [InlineData("""{"room":"ABCD1234"}""")]
    [InlineData("""{"room":null}""")]
    [InlineData("""{"room":123}""")]
    [InlineData("""not-json""")]
    public void TryGetRoomRejectsInvalidJoinPayloads(string json)
    {
        Assert.False(SyncJoinRequest.TryGetRoom(json, out _));
    }
}
