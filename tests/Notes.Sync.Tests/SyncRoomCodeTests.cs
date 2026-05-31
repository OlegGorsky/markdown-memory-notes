using Notes.Sync;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncRoomCodeTests
{
    [Theory]
    [InlineData("ABCD1234")]
    [InlineData("ROOM-2026")]
    [InlineData("team_notes")]
    public void IsValidAcceptsBoundedRoomCodes(string room)
    {
        Assert.True(SyncRoomCode.IsValid(room));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("room with space")]
    [InlineData("../secret")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    public void IsValidRejectsUnsafeRoomCodes(string room)
    {
        Assert.False(SyncRoomCode.IsValid(room));
    }
}
