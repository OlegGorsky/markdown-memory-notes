using Microsoft.JSInterop;
using Notes.Core.Files;
using Notes.Core.Sync;
using System.Text.Json;

namespace MemoryNotes.Web.Services;

public sealed class SyncClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultMaxQueuedOperations = 256;

    private readonly IJSRuntime js;
    private readonly int maxQueuedOperations;
    private readonly Lock pendingGate = new();
    private readonly Dictionary<string, SyncMessage> pendingByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> inFlightPathByMessageId = new(StringComparer.Ordinal);
    private readonly Queue<string> pendingOrder = new();
    private IJSObjectReference? _module;
    private DotNetObjectReference<SyncClient>? _selfRef;
    private string? _room;
    private Func<string, string?, string?, Task>? _onFileReceived;

    public bool IsConnected { get; private set; }
    public int PeerCount { get; private set; }
    public string? Status { get; private set; }
    public string? Room => _room;
    public event EventHandler? StateChanged;

    public SyncClient(IJSRuntime js)
        : this(js, DefaultMaxQueuedOperations)
    {
    }

    public SyncClient(IJSRuntime js, int maxQueuedOperations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueuedOperations);

        this.js = js;
        this.maxQueuedOperations = maxQueuedOperations;
    }

    public async Task ConnectAsync(string room, Func<string, string?, Task> onFileReceived)
    {
        await ConnectAsync(room, (path, content, _) => onFileReceived(path, content));
    }

    public async Task ConnectAsync(string room, Func<string, string?, string?, Task> onFileReceived)
    {
        ValidateRoom(room);
        var mod = await ModuleAsync();
        var serverUrl = await mod.InvokeAsync<string>("getDefaultSyncUrl");
        await ConnectAsync(new Uri(serverUrl), room, onFileReceived);
    }

    public async Task ConnectAsync(Uri serverUrl, string room, Func<string, string?, Task> onFileReceived)
    {
        await ConnectAsync(serverUrl, room, (path, content, _) => onFileReceived(path, content));
    }

    public async Task ConnectAsync(Uri serverUrl, string room, Func<string, string?, string?, Task> onFileReceived)
    {
        ValidateRoom(room);
        if (!string.Equals(_room, room, StringComparison.Ordinal))
        {
            ClearPending();
        }

        _room = room;
        _onFileReceived = onFileReceived;
        IsConnected = false;
        PeerCount = 0;
        Status = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _selfRef = DotNetObjectReference.Create(this);
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("connect", serverUrl.ToString(), room,
            _selfRef, nameof(OnMessage), _selfRef, nameof(OnStatus));
    }

    public async Task SendFileAsync(string relativePath, string content)
    {
        await SendFileAsync(relativePath, content, baseHash: null);
    }

    public async Task SendFileAsync(string relativePath, string content, string? baseHash)
    {
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var normalizedPath))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        ValidateBaseHash(baseHash);
        await SendOrQueueAsync(new SyncMessage("file", normalizedPath, content, baseHash, SyncMessageId.New()));
    }

    public async Task SendDeleteAsync(string relativePath)
    {
        await SendDeleteAsync(relativePath, baseHash: null);
    }

    public async Task SendDeleteAsync(string relativePath, string? baseHash)
    {
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var normalizedPath))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        ValidateBaseHash(baseHash);
        await SendOrQueueAsync(new SyncMessage("delete", normalizedPath, null, baseHash, SyncMessageId.New()));
    }

    public async Task DisconnectAsync()
    {
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("disconnect");
        IsConnected = false;
        PeerCount = 0;
        Status = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    [JSInvokable]
    public async Task OnMessage(string data)
    {
        if (SyncPresenceMessage.TryParse(data, out var peerCount))
        {
            PeerCount = peerCount;
            if (peerCount <= 1)
            {
                ClearInFlight();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            if (CanSendNow)
            {
                await FlushPendingAsync();
            }

            return;
        }

        if (SyncAckMessage.TryParse(data, out var ackedMessageId))
        {
            if (AcknowledgePending(ackedMessageId) && CanSendNow)
            {
                await FlushPendingAsync();
            }

            return;
        }

        try
        {
            var msg = JsonSerializer.Deserialize<SyncMessage>(data, JsonOptions);
            if (msg?.BaseHash is not null && !SyncContentHash.IsValid(msg.BaseHash))
            {
                return;
            }

            if (msg?.Type == "file" &&
                msg.Path is not null &&
                msg.Content is not null &&
                VaultRelativePath.TryNormalizeMarkdownContentPath(msg.Path, out var filePath))
            {
                _ = _onFileReceived?.Invoke(filePath, msg.Content, msg.BaseHash);
            }
            else if (msg?.Type == "delete" &&
                     msg.Path is not null &&
                     VaultRelativePath.TryNormalizeMarkdownContentPath(msg.Path, out var deletePath))
            {
                _ = _onFileReceived?.Invoke(deletePath, null, msg.BaseHash);
            }
        }
        catch (JsonException) { }
    }

    [JSInvokable]
    public async Task OnStatus(string status)
    {
        IsConnected = status == "connected";
        PeerCount = IsConnected ? Math.Max(PeerCount, 1) : 0;
        if (!IsConnected)
        {
            ClearInFlight();
        }

        Status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (CanSendNow)
        {
            await FlushPendingAsync();
        }
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

    private bool CanSendNow => IsConnected && PeerCount > 1;

    private static void ValidateBaseHash(string? baseHash)
    {
        if (baseHash is not null && !SyncContentHash.IsValid(baseHash))
        {
            throw new ArgumentException("Sync base hash is not valid.", nameof(baseHash));
        }
    }

    private async Task SendOrQueueAsync(SyncMessage message)
    {
        QueuePending(message);

        if (CanSendNow)
        {
            await FlushPendingAsync();
        }
    }

    private async Task FlushPendingAsync()
    {
        while (CanSendNow)
        {
            var message = NextPendingToSend();
            if (message is null)
            {
                return;
            }

            try
            {
                if (await TrySendNowAsync(message))
                {
                    MarkInFlight(message);
                    continue;
                }

                MarkDisconnected();
                return;
            }
            catch (JSException)
            {
                MarkDisconnected();
                return;
            }
        }
    }

    private async Task<bool> TrySendNowAsync(SyncMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var mod = await ModuleAsync();
        return await mod.InvokeAsync<bool>("send", json);
    }

    private void MarkDisconnected()
    {
        IsConnected = false;
        PeerCount = 0;
        ClearInFlight();
        Status = "disconnected";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueuePending(SyncMessage message)
    {
        if (_room is null ||
            message.Path is null ||
            !SyncMessageId.IsValid(message.MessageId))
        {
            return;
        }

        lock (pendingGate)
        {
            if (pendingByPath.TryGetValue(message.Path, out var previousMessage))
            {
                RemoveInFlight(previousMessage.MessageId);
            }
            else
            {
                while (pendingByPath.Count >= maxQueuedOperations && pendingOrder.TryDequeue(out var oldPath))
                {
                    if (pendingByPath.Remove(oldPath, out var removedMessage))
                    {
                        RemoveInFlight(removedMessage.MessageId);
                    }
                }

                pendingOrder.Enqueue(message.Path);
            }

            pendingByPath[message.Path] = message;
        }
    }

    private SyncMessage? NextPendingToSend()
    {
        lock (pendingGate)
        {
            foreach (var path in pendingOrder)
            {
                if (pendingByPath.TryGetValue(path, out var message) &&
                    message.MessageId is not null &&
                    !inFlightPathByMessageId.ContainsKey(message.MessageId))
                {
                    return message;
                }
            }

            return null;
        }
    }

    private void MarkInFlight(SyncMessage message)
    {
        var messageId = message.MessageId ?? string.Empty;
        if (message.Path is null || !SyncMessageId.IsValid(messageId))
        {
            return;
        }

        lock (pendingGate)
        {
            if (pendingByPath.TryGetValue(message.Path, out var currentMessage) &&
                string.Equals(currentMessage.MessageId, messageId, StringComparison.Ordinal))
            {
                inFlightPathByMessageId[messageId] = message.Path;
            }
        }
    }

    private bool AcknowledgePending(string messageId)
    {
        lock (pendingGate)
        {
            if (!inFlightPathByMessageId.Remove(messageId, out var path))
            {
                return false;
            }

            if (pendingByPath.TryGetValue(path, out var currentMessage) &&
                string.Equals(currentMessage.MessageId, messageId, StringComparison.Ordinal))
            {
                pendingByPath.Remove(path);
                RebuildPendingOrder();
            }

            return true;
        }
    }

    private void ClearInFlight()
    {
        lock (pendingGate)
        {
            inFlightPathByMessageId.Clear();
        }
    }

    private void RemoveInFlight(string? messageId)
    {
        if (messageId is not null)
        {
            inFlightPathByMessageId.Remove(messageId);
        }
    }

    private void RebuildPendingOrder()
    {
        pendingOrder.Clear();
        foreach (var path in pendingByPath.Keys)
        {
            pendingOrder.Enqueue(path);
        }
    }

    private void ClearPending()
    {
        lock (pendingGate)
        {
            pendingByPath.Clear();
            inFlightPathByMessageId.Clear();
            pendingOrder.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsConnected) await DisconnectAsync();
        _selfRef?.Dispose();
        if (_module is not null) await _module.DisposeAsync();
    }

#pragma warning disable CA1812 // Instantiated via JSON
    private sealed record SyncMessage(string Type, string? Path, string? Content, string? BaseHash, string? MessageId);
#pragma warning restore CA1812
}
