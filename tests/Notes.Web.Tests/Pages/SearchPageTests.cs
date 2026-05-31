using System.Reflection;
using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Notes.Core.Notes;
using CoreVault = Notes.Core.Vault.Vault;
using SearchPage = MemoryNotes.Web.Pages.Search;
using Xunit;

namespace Notes.Web.Tests.Pages;

public sealed class SearchPageTests
{
    [Fact]
    public async Task DoSearchAsyncUsesExistingIndexWithoutRebuildingVault()
    {
        var js = new EmptyVaultJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);
        var session = new WebVaultSession(fileSystem);
        SetCurrentVault(session, new CoreVault("/vault"));
        var now = DateTimeOffset.Now;
        session.UpsertIndexedNote(new Note("note_existing", "Quiet memory", "/vault/notes/quiet.md", "Searchable body", now, now));
        var page = new SearchPage();
        SetProperty(page, "Session", session);
        SetField(page, "_query", "quiet");

        await InvokeDoSearchAsync(page);

        var results = GetField<List<Notes.Core.Search.SearchResult>>(page, "_results");
        var result = Assert.Single(results);
        Assert.Equal("note_existing", result.Note.Id);
        Assert.Equal(0, js.Module.EnumerateFilesCalls);
    }

    private static async Task InvokeDoSearchAsync(SearchPage page)
    {
        var method = typeof(SearchPage).GetMethod("DoSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DoSearchAsync method was not found.");

        var task = (Task)(method.Invoke(page, null)
            ?? throw new InvalidOperationException("DoSearchAsync returned null."));
        await task;
    }

    private static void SetCurrentVault(WebVaultSession session, CoreVault vault)
    {
        var property = typeof(WebVaultSession).GetProperty("CurrentVault", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("CurrentVault property was not found.");

        property.SetValue(session, vault);
    }

    private static void SetProperty<T>(object target, string name, T value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} property was not found.");

        property.SetValue(target, value);
    }

    private static void SetField<T>(object target, string name, T value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} field was not found.");

        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} field was not found.");

        return (T)(field.GetValue(target)
            ?? throw new InvalidOperationException($"{name} field returned null."));
    }

    private sealed class EmptyVaultJsRuntime : IJSRuntime
    {
        public EmptyVaultJsObjectReference Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "import")
            {
                return new ValueTask<TValue>((TValue)(object)Module);
            }

            throw new NotSupportedException(identifier);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    private sealed class EmptyVaultJsObjectReference : IJSObjectReference
    {
        public int EnumerateFilesCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return identifier switch
            {
                "directoryExists" => new ValueTask<TValue>((TValue)(object)true),
                "enumerateFiles" => EnumerateFiles<TValue>(),
                _ => throw new NotSupportedException(identifier)
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }

        private ValueTask<TValue> EnumerateFiles<TValue>()
        {
            EnumerateFilesCalls++;
            return new ValueTask<TValue>((TValue)(object)Array.Empty<string>());
        }
    }
}
