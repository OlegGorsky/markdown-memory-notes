using System.Reflection;
using Xunit;
using NotesPage = MemoryNotes.Web.Pages.Notes;

namespace Notes.Web.Tests.Pages;

public sealed class NotesSyncFeedbackTests
{
    [Fact]
    public async Task TrySendSyncChangeAsyncShowsNoticeForOversizedSyncContent()
    {
        using var page = new NotesPage();

        await InvokeTrySendSyncChangeAsync(
            page,
            () => throw new ArgumentException("Sync message is too large.", "content"));

        Assert.Equal("Файл слишком большой для синхронизации.", GetNotice(page));
    }

    [Fact]
    public async Task TrySendSyncChangeAsyncRethrowsUnexpectedArgumentExceptions()
    {
        using var page = new NotesPage();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            InvokeTrySendSyncChangeAsync(
                page,
                () => throw new ArgumentException("Invalid path.", "relativePath")));

        Assert.Equal("relativePath", exception.ParamName);
        Assert.Null(GetNotice(page));
    }

    private static async Task InvokeTrySendSyncChangeAsync(NotesPage page, Func<Task> sendAsync)
    {
        var method = typeof(NotesPage).GetMethod("TrySendSyncChangeAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TrySendSyncChangeAsync method was not found.");

        var task = (Task)(method.Invoke(page, [sendAsync])
            ?? throw new InvalidOperationException("TrySendSyncChangeAsync returned null."));
        await task;
    }

    private static string? GetNotice(NotesPage page)
    {
        var noticeField = typeof(NotesPage).GetField("_notice", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_notice field was not found.");

        return (string?)noticeField.GetValue(page);
    }
}
