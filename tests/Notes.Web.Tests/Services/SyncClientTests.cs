using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Notes.Core.Sync;
using System.Text;
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
    public async Task OnMessageAwaitsRemoteFileHandlerBeforeCompleting()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", async (_, _, _) =>
        {
            handlerStarted.SetResult();
            await handlerMayFinish.Task;
        });

        var messageTask = client.OnMessage("""{"type":"file","path":"notes/project.md","content":"# Project"}""");

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(messageTask.IsCompleted);

        handlerMayFinish.SetResult();
        await messageTask;
    }

    [Fact]
    public async Task OnMessageSerializesRemoteFileHandlers()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", async (path, _, _) =>
        {
            if (path == "notes/first.md")
            {
                firstStarted.SetResult();
                await firstMayFinish.Task;
                return;
            }

            if (path == "notes/second.md")
            {
                secondStarted.SetResult();
            }
        });

        var firstMessageTask = client.OnMessage("""{"type":"file","path":"notes/first.md","content":"# First"}""");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var secondMessageTask = client.OnMessage("""{"type":"file","path":"notes/second.md","content":"# Second"}""");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(secondStarted.Task.IsCompleted);

        firstMayFinish.SetResult();
        await firstMessageTask;
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await secondMessageTask;
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
    public async Task SendFileAsyncRejectsOversizedOutgoingMessages()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(
            js,
            maxQueuedOperations: 256,
            ackTimeout: TimeSpan.FromSeconds(10),
            maxOutgoingMessageBytes: 256);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendFileAsync("notes/project.md", new string('x', 512)));

        Assert.Equal("content", exception.ParamName);
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
    public async Task OnMessageRejectsDeleteMessagesWithContentPayload()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var received = new List<(string Path, string? Content, string? BaseHash)>();
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (path, content, baseHash) =>
        {
            received.Add((path, content, baseHash));
            return Task.CompletedTask;
        });

        await client.OnMessage("""{"type":"delete","path":"notes/project.md","content":"unneeded payload"}""");

        Assert.Empty(received);
    }

    [Theory]
    [InlineData("""{"type":"file","Type":"delete","path":"notes/project.md","content":"# Project"}""")]
    [InlineData("""{"type":"file","path":"notes/project.md","Path":"notes/other.md","content":"# Project"}""")]
    [InlineData("""{"type":"file","path":"notes/project.md","content":"# Project","Content":"# Other"}""")]
    [InlineData("""{"type":"file","path":"notes/project.md","content":"# Project","baseHash":null,"BaseHash":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""")]
    [InlineData("""{"type":"file","path":"notes/project.md","content":"# Project","messageId":null,"MessageId":"0123456789abcdef0123456789abcdef"}""")]
    public async Task OnMessageRejectsDuplicateProtocolProperties(string json)
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var received = new List<(string Path, string? Content, string? BaseHash)>();
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (path, content, baseHash) =>
        {
            received.Add((path, content, baseHash));
            return Task.CompletedTask;
        });

        await client.OnMessage(json);

        Assert.Empty(received);
    }

    [Fact]
    public async Task SendRepairRequestAsyncUsesCamelCaseProtocol()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendRepairRequestAsync(new SyncRepairManifest(
            [new SyncManifestEntry("notes/project.md", SyncContentHash.Compute("# Project"))],
            Truncated: false));

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"type\":\"repairRequest\"", message, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[", message, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/project.md\"", message, StringComparison.Ordinal);
        Assert.Contains("\"hash\":\"", message, StringComparison.Ordinal);
        Assert.Contains("\"truncated\":false", message, StringComparison.Ordinal);
        Assert.Contains("\"messageId\":\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRepairRequestAsyncTrimsManifestToOutgoingMessageBudget()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(
            js,
            maxQueuedOperations: 256,
            ackTimeout: TimeSpan.FromSeconds(10),
            maxOutgoingMessageBytes: 260);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        await client.SendRepairRequestAsync(new SyncRepairManifest(
            [
                new SyncManifestEntry("notes/a.md", SyncContentHash.Compute("# A")),
                new SyncManifestEntry("notes/b.md", SyncContentHash.Compute("# B")),
                new SyncManifestEntry("notes/c.md", SyncContentHash.Compute("# C"))
            ],
            Truncated: false));

        var message = Assert.Single(js.Module.SentMessages);
        Assert.True(Encoding.UTF8.GetByteCount(message) <= 260);
        Assert.True(SyncRepairRequestMessage.TryParse(message, out var request));
        Assert.True(request.Truncated);
        Assert.True(request.Entries.Count is > 0 and < 3);
    }

    [Fact]
    public async Task TrySendRepairFileAsyncDoesNotEvictQueuedUserChangesWhenQueueIsFull()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 1);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.SendFileAsync("notes/user.md", "# User");

        var queued = await client.TrySendRepairFileAsync("notes/repair.md", "# Repair", baseHash: null);

        Assert.False(queued);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");
        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"path\":\"notes/user.md\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("notes/repair.md", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySendRepairFileAsyncDoesNotReplaceQueuedUserChangeForSamePath()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 2);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.SendFileAsync("notes/project.md", "# User");

        var queued = await client.TrySendRepairFileAsync("notes/project.md", "# Repair", baseHash: null);

        Assert.False(queued);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");
        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"content\":\"# User\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("# Repair", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySendRepairFileAsyncQueuesWhenCapacityIsAvailable()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js, maxQueuedOperations: 2);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.SendFileAsync("notes/user.md", "# User");

        var queued = await client.TrySendRepairFileAsync("notes/repair.md", "# Repair", baseHash: null);

        Assert.True(queued);
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");
        Assert.Equal(2, js.Module.SentMessages.Count);
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/user.md\"", StringComparison.Ordinal));
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/repair.md\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnMessageInvokesRepairRequestHandler()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var requests = new List<SyncRepairRequest>();
        var hash = SyncContentHash.Compute("# Remote");
        await client.ConnectAsync(
            new Uri("ws://localhost:5199/sync"),
            "AbCdEfGhIjKlMnOpQrStUv",
            (_, _, _) => Task.CompletedTask,
            request =>
            {
                requests.Add(request);
                return Task.CompletedTask;
            });

        await client.OnMessage($$"""{"type":"repairRequest","entries":[{"path":"notes/project.md","hash":"{{hash}}"}],"truncated":true}""");

        var request = Assert.Single(requests);
        Assert.True(request.Truncated);
        var entry = Assert.Single(request.Entries);
        Assert.Equal("notes/project.md", entry.Path);
        Assert.Equal(hash, entry.Hash);
    }

    [Fact]
    public async Task PeerAppearanceRequestsRepairOncePerConnection()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        var repairRequests = 0;
        client.RepairRequested = () =>
        {
            repairRequests++;
            return Task.CompletedTask;
        };

        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");

        Assert.Equal(0, repairRequests);

        await client.OnMessage("""{"type":"presence","peerCount":2}""");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(1, repairRequests);

        await client.OnStatus("disconnected");
        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(2, repairRequests);
    }

    [Fact]
    public async Task PeerAppearanceRetriesRepairRequestWhenHandlerFails()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        var repairRequests = 0;
        client.RepairRequested = () =>
        {
            repairRequests++;
            if (repairRequests == 1)
            {
                throw new InvalidOperationException("repair scan failed");
            }

            return Task.CompletedTask;
        };

        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.OnStatus("connected");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.OnMessage("""{"type":"presence","peerCount":2}"""));

        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(2, repairRequests);
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
    public async Task ConnectAsyncReusesDotNetCallbackReferenceAcrossReconnects()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);

        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        Assert.Equal(2, js.Module.ConnectCallbackReferences.Count);
        Assert.Same(js.Module.ConnectCallbackReferences[0], js.Module.ConnectCallbackReferences[1]);
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
    public async Task SendFileAsyncBoundsDefaultInFlightWindowUntilAcked()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        for (var i = 0; i < 17; i++)
        {
            await client.SendFileAsync($"notes/{i:00}.md", $"# Note {i}");
        }

        await client.OnStatus("connected");
        await client.OnMessage("""{"type":"presence","peerCount":2}""");

        Assert.Equal(16, js.Module.SentMessages.Count);
        var firstMessageId = ReadMessageId(js.Module.SentMessages[0]);

        await client.OnMessage($$"""{"type":"ack","messageId":"{{firstMessageId}}"}""");

        Assert.Equal(17, js.Module.SentMessages.Count);
        Assert.Contains("\"path\":\"notes/16.md\"", js.Module.SentMessages[^1], StringComparison.Ordinal);
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

    [Fact]
    public async Task DisposeAsyncDisconnectsSocketAfterConnectBeforeConnectedStatus()
    {
        var js = new CapturingJsRuntime();
        await using (var client = new SyncClient(js))
        {
            await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
        }

        Assert.Equal(1, js.Module.ConnectCalls);
        Assert.Equal(1, js.Module.DisconnectCalls);
    }

    [Fact]
    public async Task DisposeAsyncDoesNotDisconnectAgainAfterExplicitDisconnect()
    {
        var js = new CapturingJsRuntime();
        await using (var client = new SyncClient(js))
        {
            await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);
            await client.DisconnectAsync();
        }

        Assert.Equal(1, js.Module.ConnectCalls);
        Assert.Equal(1, js.Module.DisconnectCalls);
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
        public List<object?> ConnectCallbackReferences { get; } = new();
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
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
                    if (args is [_, _, var onMessageReference, _, var onStatusReference, _])
                    {
                        Assert.Same(onMessageReference, onStatusReference);
                        ConnectCallbackReferences.Add(onMessageReference);
                    }
                }
                else
                {
                    DisconnectCalls++;
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
