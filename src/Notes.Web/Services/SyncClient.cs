using Microsoft.JSInterop;
using Notes.Core.Files;
using Notes.Core.Sync;
using System.Text.Json;

namespace MemoryNotes.Web.Services;

public sealed class SyncClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<SyncClient>? _selfRef;
    private string? _room;
    private Func<string, string?, Task>? _onFileReceived;

    public bool IsConnected { get; private set; }
    public string? Status { get; private set; }
    public event EventHandler? StateChanged;

    public SyncClient(IJSRuntime js)
    {
        this.js = js;
    }

    public async Task ConnectAsync(string room, Func<string, string?, Task> onFileReceived)
    {
        ValidateRoom(room);
        var mod = await ModuleAsync();
        var serverUrl = await mod.InvokeAsync<string>("getDefaultSyncUrl");
        await ConnectAsync(new Uri(serverUrl), room, onFileReceived);
    }

    public async Task ConnectAsync(Uri serverUrl, string room, Func<string, string?, Task> onFileReceived)
    {
        ValidateRoom(room);
        _room = room;
        _onFileReceived = onFileReceived;
        _selfRef = DotNetObjectReference.Create(this);
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("connect", serverUrl.ToString(), room,
            _selfRef, nameof(OnMessage), _selfRef, nameof(OnStatus));
    }

    public async Task SendFileAsync(string relativePath, string content)
    {
        if (!IsConnected) return;
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var normalizedPath))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        var msg = JsonSerializer.Serialize(new SyncMessage("file", normalizedPath, content), JsonOptions);
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("send", msg);
    }

    public async Task SendDeleteAsync(string relativePath)
    {
        if (!IsConnected) return;
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var normalizedPath))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        var msg = JsonSerializer.Serialize(new SyncMessage("delete", normalizedPath, null), JsonOptions);
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("send", msg);
    }

    public async Task DisconnectAsync()
    {
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("disconnect");
        IsConnected = false;
        Status = null;
    }

    [JSInvokable]
    public void OnMessage(string data)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<SyncMessage>(data, JsonOptions);
            if (msg?.Type == "file" &&
                msg.Path is not null &&
                msg.Content is not null &&
                VaultRelativePath.TryNormalizeMarkdownContentPath(msg.Path, out var filePath))
            {
                _ = _onFileReceived?.Invoke(filePath, msg.Content);
            }
            else if (msg?.Type == "delete" &&
                     msg.Path is not null &&
                     VaultRelativePath.TryNormalizeMarkdownContentPath(msg.Path, out var deletePath))
            {
                _ = _onFileReceived?.Invoke(deletePath, null);
            }
        }
        catch (JsonException) { }
    }

    [JSInvokable]
    public void OnStatus(string status)
    {
        IsConnected = status == "connected";
        Status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IJSObjectReference> ModuleAsync()
    {
        return _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/sync-client.js");
    }

    private static void ValidateRoom(string room)
    {
        if (!SyncPairingCode.IsValid(room))
        {
            throw new ArgumentException("Sync room code is not a valid pairing code.", nameof(room));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsConnected) await DisconnectAsync();
        _selfRef?.Dispose();
        if (_module is not null) await _module.DisposeAsync();
    }

#pragma warning disable CA1812 // Instantiated via JSON
    private sealed record SyncMessage(string Type, string? Path, string? Content);
#pragma warning restore CA1812
}
