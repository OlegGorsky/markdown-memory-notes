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
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
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
    public async Task SendFileAsyncRequeuesWhenJsSendReportsClosedSocket()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");
        js.Module.SendReportsOpen = false;

        await client.SendFileAsync("notes/project.md", "# Waiting");

        Assert.False(client.IsConnected);
        Assert.Equal(0, client.PeerCount);
        Assert.Empty(js.Module.SentMessages);

        js.Module.SendReportsOpen = true;
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"content\":\"# Waiting\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendFileAsyncRetriesAfterReconnectUntilAcked()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendFileAsync("notes/project.md", "# Waiting");
        var firstMessage = Assert.Single(js.Module.SentMessages);
        var messageId = ReadMessageId(firstMessage);

        await client.OnStatus("disconnected");
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(2, js.Module.SentMessages.Count);
        Assert.Equal(messageId, ReadMessageId(js.Module.SentMessages[1]));
    }

    [Fact]
    public async Task AckedMessageIsNotRetriedAfterReconnect()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendFileAsync("notes/project.md", "# Waiting");
        var messageId = ReadMessageId(Assert.Single(js.Module.SentMessages));
        await client.OnMessage($$"""{"type":"ack","messageId":"{{messageId}}"}""");

        await client.OnStatus("disconnected");
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Single(js.Module.SentMessages);
    }

    [Fact]
    public async Task SendFileAsyncRetriesWhenAckTimeoutExpires()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 256, ackTimeout: TimeSpan.FromMilliseconds(20));
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendFileAsync("notes/project.md", "# Waiting");
        var messageId = ReadMessageId(Assert.Single(js.Module.SentMessages));

        await WaitUntilAsync(() => js.Module.SentMessages.Count >= 2);

        Assert.Equal(messageId, ReadMessageId(js.Module.SentMessages[1]));
        await client.OnMessage($$"""{"type":"ack","messageId":"{{messageId}}"}""");
    }

    [Fact]
    public async Task AckedMessageIsNotRetriedAfterAckTimeout()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 256, ackTimeout: TimeSpan.FromMilliseconds(20));
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendFileAsync("notes/project.md", "# Waiting");
        var messageId = ReadMessageId(Assert.Single(js.Module.SentMessages));
        await client.OnMessage($$"""{"type":"ack","messageId":"{{messageId}}"}""");
        await Task.Delay(80, TestContext.Current.CancellationToken);

        Assert.Single(js.Module.SentMessages);
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
        public bool SendReportsOpen { get; set; } = true;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "send" && args is [string message])
            {
                if (!SendReportsOpen)
                {
                    return typeof(TValue) == typeof(bool)
                        ? new ValueTask<TValue>((TValue)(object)false)
                        : new ValueTask<TValue>(default(TValue)!);
                }

                SentMessages.Add(message);
                return typeof(TValue) == typeof(bool)
                    ? new ValueTask<TValue>((TValue)(object)true)
                    : new ValueTask<TValue>(default(TValue)!);
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

    private static string ReadMessageId(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("messageId").GetString() ?? string.Empty;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
