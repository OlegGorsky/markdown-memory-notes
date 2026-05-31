using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace Notes.Web.Tests.Services;

public sealed class SyncClientTests
{
    [Fact]
    public async Task SendFileAsyncUsesCamelCaseProtocol()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var baseHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        await client.SendFileAsync("""notes\project.md""", "# Project", baseHash);

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"type\":\"file\"", message, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/project.md\"", message, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"# Project\"", message, StringComparison.Ordinal);
        Assert.Contains("\"baseHash\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnMessageAcceptsCamelCaseProtocol()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var received = new List<(string Path, string? Content, string? BaseHash)>();
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (path, content, baseHash) =>
        {
            received.Add((path, content, baseHash));
            return Task.CompletedTask;
        });

        await client.OnMessage("""{"type":"file","path":"notes/project.md","content":"# Project","baseHash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""");

        var item = Assert.Single(received);
        Assert.Equal("notes/project.md", item.Path);
        Assert.Equal("# Project", item.Content);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", item.BaseHash);
    }

    [Fact]
    public async Task ConnectAsyncRejectsWeakRoomCodesBeforeOpeningSocket()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "ABCD1234", (_, _) => Task.CompletedTask));

        Assert.Equal("room", exception.ParamName);
        Assert.Equal(0, js.Module.ConnectCalls);
    }

    [Fact]
    public async Task SendFileAsyncRejectsInvalidBaseHash()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendFileAsync("notes/project.md", "# Project", "bad hash"));

        Assert.Equal("baseHash", exception.ParamName);
        Assert.Empty(js.Module.SentMessages);
    }

    [Fact]
    public async Task OnMessageIgnoresInvalidBaseHash()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var received = new List<(string Path, string? Content, string? BaseHash)>();
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (path, content, baseHash) =>
        {
            received.Add((path, content, baseHash));
            return Task.CompletedTask;
        });

        await client.OnMessage("""{"type":"file","path":"notes/project.md","content":"# Project","baseHash":"bad hash"}""");

        Assert.Empty(received);
    }

    [Fact]
    public async Task SendFileAsyncQueuesLatestChangeAndFlushesWhenPeerAppears()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        await client.SendFileAsync("notes/project.md", "# Draft");
        await client.SendFileAsync("notes/project.md", "# Final");

        Assert.Empty(js.Module.SentMessages);

        await client.OnStatus("connected");

        Assert.Empty(js.Module.SentMessages);

        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"type\":\"file\"", message, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/project.md\"", message, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"# Final\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("# Draft", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendDeleteAsyncReplacesQueuedFileChange()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        await client.SendFileAsync("notes/project.md", "# Draft");
        await client.SendDeleteAsync("notes/project.md");
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"type\":\"delete\"", message, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/project.md\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("# Draft", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendFileAsyncBoundsQueuedPathsByDroppingOldestPath()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 2);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        await client.SendFileAsync("notes/a.md", "# A");
        await client.SendFileAsync("notes/b.md", "# B");
        await client.SendFileAsync("notes/c.md", "# C");
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(2, js.Module.SentMessages.Count);
        Assert.DoesNotContain(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/a.md\"", StringComparison.Ordinal));
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/b.md\"", StringComparison.Ordinal));
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/c.md\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendFileAsyncQueuesWhileConnectedAlone()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":1}""");

        await client.SendFileAsync("notes/project.md", "# Alone");

        Assert.True(client.IsConnected);
        Assert.Equal(1, client.PeerCount);
        Assert.Empty(js.Module.SentMessages);

        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"content\":\"# Alone\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsyncResetsPeerPresenceBeforeReconnecting()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "ZyXwVuTsRqPoNmLkJiHgFe", (_, _) => Task.CompletedTask);
        await client.SendFileAsync("notes/project.md", "# Waiting");

        Assert.False(client.IsConnected);
        Assert.Equal(0, client.PeerCount);
        Assert.Empty(js.Module.SentMessages);

        await client.OnStatus("connected");

        Assert.Empty(js.Module.SentMessages);

        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"content\":\"# Waiting\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PresenceBeforeConnectedStatusStillFlushesQueuedChanges()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        await client.SendFileAsync("notes/project.md", "# Waiting");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Empty(js.Module.SentMessages);

        await client.OnStatus("connected");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"content\":\"# Waiting\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisconnectAsyncClearsPeerPresence()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.DisconnectAsync();

        Assert.False(client.IsConnected);
        Assert.Equal(0, client.PeerCount);
    }

    private sealed class CapturingJsRuntime : IJSRuntime
    {
        public CapturingJsObjectReference Module { get; } = new();

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

    private sealed class CapturingJsObjectReference : IJSObjectReference
    {
        public List<string> SentMessages { get; } = new();
        public int ConnectCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "send" && args is [string message])
            {
                SentMessages.Add(message);
                return new ValueTask<TValue>(default(TValue)!);
            }

            if (identifier is "connect" or "disconnect")
            {
                if (identifier == "connect")
                {
                    ConnectCalls++;
                }

                return new ValueTask<TValue>(default(TValue)!);
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
}
