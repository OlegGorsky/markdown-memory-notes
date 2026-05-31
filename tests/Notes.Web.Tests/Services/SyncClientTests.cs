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
        client.OnStatus("connected");

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
