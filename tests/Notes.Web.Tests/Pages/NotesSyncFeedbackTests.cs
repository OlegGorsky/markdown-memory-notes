using System.Reflection;
using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Notes.Core.Notes;
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

    [Fact]
    public async Task RemoveLoadedNoteClearsStaleQuietMemory()
    {
        using var page = new NotesPage();
        await using var fileSystem = new BrowserFileSystem(new NoopJsRuntime());
        var now = DateTimeOffset.Now;
        SetField(page, "_notes", new List<Note>
        {
            new("note_a", "Deleted", "/notes/deleted.md", "deleted body", now, now)
        });
        SetField(page, "_quietMemory", new List<string> { "Old suggestion" });
        SetSession(page, new WebVaultSession(fileSystem));

        InvokeRemoveLoadedNoteCore(page, "note_a");

        Assert.Empty(GetField<List<Note>>(page, "_notes"));
        Assert.Empty(GetField<List<string>>(page, "_quietMemory"));
    }

    [Fact]
    public async Task RemoveLoadedNoteKeepsQuietMemoryForSelectedNote()
    {
        using var page = new NotesPage();
        await using var fileSystem = new BrowserFileSystem(new NoopJsRuntime());
        var now = DateTimeOffset.Now;
        var selected = new Note("note_selected", "Selected", "/notes/selected.md", "selected body", now, now);
        SetField(page, "_notes", new List<Note>
        {
            selected,
            new("note_deleted", "Deleted", "/notes/deleted.md", "deleted body", now, now)
        });
        SetField(page, "_selectedNote", selected);
        SetField(page, "_quietMemory", new List<string> { "Selected suggestion" });
        SetSession(page, new WebVaultSession(fileSystem));

        InvokeRemoveLoadedNoteCore(page, "note_deleted");

        Assert.Single(GetField<List<Note>>(page, "_notes"));
        Assert.Equal("Selected suggestion", Assert.Single(GetField<List<string>>(page, "_quietMemory")));
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

    private static void InvokeRemoveLoadedNoteCore(NotesPage page, string noteId)
    {
        var method = typeof(NotesPage).GetMethod("RemoveLoadedNoteCore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RemoveLoadedNoteCore method was not found.");

        method.Invoke(page, [noteId]);
    }

    private static void SetSession(NotesPage page, WebVaultSession session)
    {
        var property = typeof(NotesPage).GetProperty("Session", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Session property was not found.");

        property.SetValue(page, session);
    }

    private static void SetField<T>(NotesPage page, string name, T value)
    {
        var field = typeof(NotesPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} field was not found.");

        field.SetValue(page, value);
    }

    private static T GetField<T>(NotesPage page, string name)
    {
        var field = typeof(NotesPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} field was not found.");

        return (T)(field.GetValue(page)
            ?? throw new InvalidOperationException($"{name} field returned null."));
    }

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            throw new NotSupportedException("JS interop is not used by this test.");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            throw new NotSupportedException("JS interop is not used by this test.");
        }
    }
}
