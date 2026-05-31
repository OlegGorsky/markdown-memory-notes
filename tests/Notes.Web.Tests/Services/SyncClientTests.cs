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

        await client.SendFileAsync("""notes\project.md""", "# Project");

        var message = Assert.Single(js.Module.SentMessages);
        Assert.Contains("\"type\":\"file\"", message, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"notes/project.md\"", message, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"# Project\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnMessageAcceptsCamelCaseProtocol()
    {
        await using var client = new SyncClient(new CapturingJsRuntime());
        var received = new List<(string Path, string? Content)>();
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (path, content) =>
        {
            received.Add((path, content));
            return Task.CompletedTask;
        });

        client.OnMessage("""{"type":"file","path":"notes/project.md","content":"# Project"}""");

        var item = Assert.Single(received);
        Assert.Equal("notes/project.md", item.Path);
        Assert.Equal("# Project", item.Content);
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
    public async Task SendFileAsyncQueuesLatestDisconnectedChangeAndFlushesWhenConnected()
    {
        var js = new CapturingJsRuntime();
        await using var client = new SyncClient(js);
        await client.ConnectAsync(new Uri("ws://localhost:5199/sync"), "AbCdEfGhIjKlMnOpQrStUv", (_, _) => Task.CompletedTask);

        await client.SendFileAsync("notes/project.md", "# Draft");
        await client.SendFileAsync("notes/project.md", "# Final");

        Assert.Empty(js.Module.SentMessages);

        await client.OnStatus("connected");

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

        Assert.Equal(2, js.Module.SentMessages.Count);
        Assert.DoesNotContain(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/a.md\"", StringComparison.Ordinal));
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/b.md\"", StringComparison.Ordinal));
        Assert.Contains(js.Module.SentMessages, message => message.Contains("\"path\":\"notes/c.md\"", StringComparison.Ordinal));
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
