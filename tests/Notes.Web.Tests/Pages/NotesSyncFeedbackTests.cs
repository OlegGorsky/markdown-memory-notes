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
    public async Task UpdateSyncStatusNoticeShowsOverloadedStatusUntilConnected()
    {
        using var page = new NotesPage();
        await using var sync = new SyncClient(new NoopJsRuntime());
        SetProperty(page, "Sync", sync);

        await sync.OnStatus("overloaded");
        InvokeUpdateSyncStatusNotice(page);

        Assert.Equal("Синхронизация перегружена. Переподключаемся.", GetNotice(page));

        await sync.OnStatus("connected");
        InvokeUpdateSyncStatusNotice(page);

        Assert.Null(GetNotice(page));
    }

    [Fact]
    public async Task GenerateSyncCodeForModalConnectsActiveVault()
    {
        var js = new CapturingSyncSetupJsRuntime();
        using var page = new NotesPage();
        await using var sync = new SyncClient(js);
        await using var vaultManager = new VaultManager(js);
        SetProperty(page, "Sync", sync);
        SetProperty(page, "VaultMgr", vaultManager);
        SetField(page, "_vaults", new List<VaultEntry>
        {
            new("vault_1", "Vault", "/vault", null)
        });
        SetField(page, "_activeVaultId", "vault_1");
        SetField(page, "_syncModalVault", "vault_1");

        await InvokeGenerateSyncCodeForModalAsync(page, ignoreUnrenderedStateChange: true);

        var saved = Assert.Single(js.Module.SavedVaults);
        Assert.NotNull(saved.SyncCode);
        Assert.Equal(saved.SyncCode, Assert.Single(js.Module.ConnectRooms));
        Assert.Equal(saved.SyncCode, sync.Room);
    }

    [Fact]
    public async Task ConnectVaultSyncIfActiveDisconnectsWhenActiveVaultHasNoSyncCode()
    {
        var js = new CapturingSyncSetupJsRuntime();
        using var page = new NotesPage();
        await using var sync = new SyncClient(js);
        await sync.ConnectAsync(
            new Uri("ws://localhost:5199/sync"),
            "AbCdEfGhIjKlMnOpQrStUv",
            (_, _, _) => Task.CompletedTask);
        SetProperty(page, "Sync", sync);
        SetField(page, "_activeVaultId", "vault_2");

        await InvokeConnectVaultSyncIfActiveAsync(page, new VaultEntry("vault_2", "Other", "/other", null));

        Assert.Equal(1, js.Module.DisconnectCalls);
    }

    [Fact]
    public async Task ReconnectSyncAsyncDoesNotConnectInactiveVault()
    {
        var js = new CapturingSyncSetupJsRuntime();
        using var page = new NotesPage();
        await using var sync = new SyncClient(js);
        SetProperty(page, "Sync", sync);
        SetField(page, "_activeVaultId", "vault_1");

        await InvokeReconnectSyncAsync(page, new VaultEntry("vault_2", "Other", "/other", "AbCdEfGhIjKlMnOpQrStUv"));

        Assert.Empty(js.Module.ConnectRooms);
    }

    [Fact]
    public async Task ConnectNewVaultAsyncCreatesVirtualVaultWithoutOpeningPicker()
    {
        const string syncCode = "AbCdEfGhIjKlMnOpQrStUv";
        var js = new CapturingSyncSetupJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);
        var session = new WebVaultSession(fileSystem);
        await using var vaultManager = new VaultManager(js);
        await using var sync = new SyncClient(js);
        using var page = new NotesPage();
        SetProperty(page, "FS", fileSystem);
        SetProperty(page, "Session", session);
        SetProperty(page, "VaultMgr", vaultManager);
        SetProperty(page, "Sync", sync);
        SetField(page, "_syncCodeInput", syncCode);

        await InvokeConnectNewVaultAsync(page, ignoreUnrenderedStateChange: true);

        Assert.Equal(0, js.Module.OpenVaultCalls);
        Assert.Equal(1, js.Module.CreateVirtualVaultCalls);
        var saved = Assert.Single(js.Module.SavedVaults);
        Assert.Equal("paired_vault", saved.Id);
        Assert.Equal(syncCode, saved.SyncCode);
        Assert.Equal(syncCode, Assert.Single(js.Module.ConnectRooms));
        Assert.Equal(syncCode, sync.Room);
        Assert.True(session.IsOpen);
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

    private static void InvokeUpdateSyncStatusNotice(NotesPage page)
    {
        var method = typeof(NotesPage).GetMethod("UpdateSyncStatusNotice", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("UpdateSyncStatusNotice method was not found.");

        method.Invoke(page, []);
    }

    private static async Task InvokeGenerateSyncCodeForModalAsync(
        NotesPage page,
        bool ignoreUnrenderedStateChange = false)
    {
        var method = typeof(NotesPage).GetMethod("GenerateSyncCodeForModalAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GenerateSyncCodeForModalAsync method was not found.");

        var task = (Task)(method.Invoke(page, [])
            ?? throw new InvalidOperationException("GenerateSyncCodeForModalAsync returned null."));
        try
        {
            await task;
        }
        catch (InvalidOperationException exception) when (
            ignoreUnrenderedStateChange &&
            exception.Message.Contains("render handle", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static async Task InvokeConnectVaultSyncIfActiveAsync(NotesPage page, VaultEntry vault)
    {
        var method = typeof(NotesPage).GetMethod("ConnectVaultSyncIfActiveAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ConnectVaultSyncIfActiveAsync method was not found.");

        var task = (Task)(method.Invoke(page, [vault])
            ?? throw new InvalidOperationException("ConnectVaultSyncIfActiveAsync returned null."));
        await task;
    }

    private static async Task InvokeReconnectSyncAsync(NotesPage page, VaultEntry vault)
    {
        var method = typeof(NotesPage).GetMethod("ReconnectSyncAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ReconnectSyncAsync method was not found.");

        var task = (Task)(method.Invoke(page, [vault])
            ?? throw new InvalidOperationException("ReconnectSyncAsync returned null."));
        await task;
    }

    private static async Task InvokeConnectNewVaultAsync(
        NotesPage page,
        bool ignoreUnrenderedStateChange = false)
    {
        var method = typeof(NotesPage).GetMethod("ConnectNewVaultAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ConnectNewVaultAsync method was not found.");

        var task = (Task)(method.Invoke(page, [])
            ?? throw new InvalidOperationException("ConnectNewVaultAsync returned null."));
        try
        {
            await task;
        }
        catch (InvalidOperationException exception) when (
            ignoreUnrenderedStateChange &&
            exception.Message.Contains("render handle", StringComparison.OrdinalIgnoreCase))
        {
        }
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

    private static void SetProperty<T>(NotesPage page, string name, T value)
    {
        var property = typeof(NotesPage).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} property was not found.");

        property.SetValue(page, value);
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

    private sealed class CapturingSyncSetupJsRuntime : IJSRuntime
    {
        public CapturingSyncSetupJsObjectReference Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "import")
            {
                return new ValueTask<TValue>((TValue)(object)Module);
            }

            throw new NotSupportedException(identifier);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    private sealed class CapturingSyncSetupJsObjectReference : IJSObjectReference
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        public List<VaultEntry> SavedVaults { get; } = new();
        public List<string> ConnectRooms { get; } = new();
        public int DisconnectCalls { get; private set; }
        public int OpenVaultCalls { get; private set; }
        public int CreateVirtualVaultCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return identifier switch
            {
                "saveVault" => SaveVault<TValue>(args),
                "listVaults" => Result<TValue>(SavedVaults.ToArray()),
                "openVault" => OpenVault<TValue>(),
                "createVirtualVault" => CreateVirtualVault<TValue>(),
                "directoryExists" => Result<TValue>(DirectoryExists(args)),
                "fileExists" => Result<TValue>(files.ContainsKey(PathArg(args))),
                "createDirectory" => Result<TValue>(default!),
                "writeAllText" => WriteAllText<TValue>(args),
                "readAllText" => Result<TValue>(files[PathArg(args)]),
                "enumerateFiles" => Result<TValue>(Array.Empty<string>()),
                "getDefaultSyncUrl" => Result<TValue>("ws://localhost:5199/sync"),
                "connect" => Connect<TValue>(args),
                "disconnect" => Disconnect<TValue>(),
                _ => throw new NotSupportedException(identifier)
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }

        private ValueTask<TValue> SaveVault<TValue>(object?[]? args)
        {
            SavedVaults.Add((VaultEntry)(args?[0]
                ?? throw new ArgumentException("Missing vault.", nameof(args))));
            return Result<TValue>(default!);
        }

        private ValueTask<TValue> OpenVault<TValue>()
        {
            OpenVaultCalls++;
            return Result<TValue>(new BrowserVaultHandle("opened_vault", "Opened", "/browser-vaults/opened_vault"));
        }

        private ValueTask<TValue> CreateVirtualVault<TValue>()
        {
            CreateVirtualVaultCalls++;
            return Result<TValue>(new BrowserVaultHandle("paired_vault", "Хранилище", "/browser-vaults/paired_vault"));
        }

        private bool DirectoryExists(object?[]? args)
        {
            var path = PathArg(args);
            return string.IsNullOrEmpty(path) ||
                   path is "notes" or "inbox" or ".notes" ||
                   path.StartsWith("notes/", StringComparison.Ordinal) ||
                   path.StartsWith("inbox/", StringComparison.Ordinal) ||
                   path.StartsWith(".notes/", StringComparison.Ordinal);
        }

        private ValueTask<TValue> WriteAllText<TValue>(object?[]? args)
        {
            files[PathArg(args)] = (string)(args?[1]
                ?? throw new ArgumentException("Missing contents.", nameof(args)));
            return Result<TValue>(default!);
        }

        private ValueTask<TValue> Connect<TValue>(object?[]? args)
        {
            ConnectRooms.Add((string)(args?[1]
                ?? throw new ArgumentException("Missing sync room.", nameof(args))));
            return Result<TValue>(default!);
        }

        private ValueTask<TValue> Disconnect<TValue>()
        {
            DisconnectCalls++;
            return Result<TValue>(default!);
        }

        private static string PathArg(object?[]? args)
        {
            return (string)(args?[0]
                ?? throw new ArgumentException("Missing path.", nameof(args)));
        }

        private static ValueTask<TValue> Result<TValue>(object? value)
        {
            return new ValueTask<TValue>((TValue)value!);
        }
    }
}
