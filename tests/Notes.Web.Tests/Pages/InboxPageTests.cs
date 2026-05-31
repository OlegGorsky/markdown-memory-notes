using System.Reflection;
using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using CoreVault = Notes.Core.Vault.Vault;
using InboxPage = MemoryNotes.Web.Pages.Inbox;
using Xunit;

namespace Notes.Web.Tests.Pages;

public sealed class InboxPageTests
{
    [Fact]
    public async Task CaptureAsyncUpdatesRecentNotesWithoutRebuildingVaultIndex()
    {
        var js = new CapturingVaultJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);
        var session = new WebVaultSession(fileSystem);
        SetCurrentVault(session, new CoreVault("/vault"));
        var page = new InboxPage();
        SetProperty(page, "Session", session);
        SetField(page, "_text", "Captured thought");

        await InvokeCaptureAsync(page);

        Assert.Equal(0, js.Module.EnumerateFilesCalls);
        var note = Assert.Single(GetField<List<Notes.Core.Notes.Note>>(page, "_recentNotes"));
        Assert.StartsWith("Inbox ", note.Title, StringComparison.Ordinal);
        Assert.Contains("Captured thought", note.Body, StringComparison.Ordinal);
    }

    private static async Task InvokeCaptureAsync(InboxPage page)
    {
        var method = typeof(InboxPage).GetMethod("CaptureAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CaptureAsync method was not found.");

        var task = (Task)(method.Invoke(page, null)
            ?? throw new InvalidOperationException("CaptureAsync returned null."));
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

    private sealed class CapturingVaultJsRuntime : IJSRuntime
    {
        public CapturingVaultJsObjectReference Module { get; } = new();

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

    private sealed class CapturingVaultJsObjectReference : IJSObjectReference
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

        public int EnumerateFilesCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return identifier switch
            {
                "directoryExists" => Result<TValue>(true),
                "fileExists" => Result<TValue>(files.ContainsKey(PathArg(args))),
                "readAllText" => Result<TValue>(files[PathArg(args)]),
                "writeAllText" => WriteAllText<TValue>(args),
                "enumerateFiles" => EnumerateFiles<TValue>(),
                _ => throw new NotSupportedException(identifier)
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }

        private ValueTask<TValue> WriteAllText<TValue>(object?[]? args)
        {
            files[PathArg(args)] = (string)(args?[1]
                ?? throw new ArgumentException("Missing file contents.", nameof(args)));
            return Result<TValue>(default!);
        }

        private ValueTask<TValue> EnumerateFiles<TValue>()
        {
            EnumerateFilesCalls++;
            return Result<TValue>(Array.Empty<string>());
        }

        private static string PathArg(object?[]? args)
        {
            return (string)(args?[0]
                ?? throw new ArgumentException("Missing path argument.", nameof(args)));
        }

        private static ValueTask<TValue> Result<TValue>(object value)
        {
            return new ValueTask<TValue>((TValue)value);
        }
    }
}
