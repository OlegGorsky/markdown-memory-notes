using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace Notes.Web.Tests.Services;

public sealed class BrowserFileSystemTests
{
    [Fact]
    public async Task WriteAllTextKeepsRelativeSyncPathsInsideCurrentVault()
    {
        var js = new CapturingFileSystemJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);
        await fileSystem.OpenVaultAsync("vault_a");

        await fileSystem.WriteAllTextAsync("notes/incoming.md", "# Incoming");

        var write = Assert.Single(js.Module.Writes);
        Assert.Equal("notes/incoming.md", write.Path);
        Assert.Equal("# Incoming", write.Contents);
    }

    [Fact]
    public async Task WriteAllTextStripsCurrentVaultRootFromAbsolutePaths()
    {
        var js = new CapturingFileSystemJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);
        await fileSystem.OpenVaultAsync("vault_a");

        await fileSystem.WriteAllTextAsync("/browser-vaults/vault_a/notes/local.md", "# Local");

        var write = Assert.Single(js.Module.Writes);
        Assert.Equal("notes/local.md", write.Path);
        Assert.Equal("# Local", write.Contents);
    }

    [Fact]
    public async Task CreateVirtualVaultAsyncUsesVirtualVaultInterop()
    {
        var js = new CapturingFileSystemJsRuntime();
        await using var fileSystem = new BrowserFileSystem(js);

        var vault = await fileSystem.CreateVirtualVaultAsync("paired_a");

        Assert.Equal("paired_a", vault!.Id);
        Assert.Equal("/browser-vaults/paired_a", vault.Path);
        Assert.Equal(0, js.Module.OpenVaultCalls);
        Assert.Equal(1, js.Module.CreateVirtualVaultCalls);
    }

    private sealed class CapturingFileSystemJsRuntime : IJSRuntime
    {
        public CapturingFileSystemJsObjectReference Module { get; } = new();

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

    private sealed class CapturingFileSystemJsObjectReference : IJSObjectReference
    {
        public List<(string Path, string Contents)> Writes { get; } = new();
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
                "openVault" => OpenVault<TValue>(),
                "createVirtualVault" => CreateVirtualVault<TValue>(args),
                "writeAllText" => WriteAllText<TValue>(args),
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

        private ValueTask<TValue> OpenVault<TValue>()
        {
            OpenVaultCalls++;
            return Result<TValue>(new BrowserVaultHandle("vault_a", "Vault A", "/browser-vaults/vault_a"));
        }

        private ValueTask<TValue> CreateVirtualVault<TValue>(object?[]? args)
        {
            CreateVirtualVaultCalls++;
            var id = (string)(args?[0] ?? "virtual_a");
            return Result<TValue>(new BrowserVaultHandle(id, "Vault", $"/browser-vaults/{id}"));
        }

        private ValueTask<TValue> WriteAllText<TValue>(object?[]? args)
        {
            Writes.Add((
                (string)(args?[0] ?? throw new ArgumentException("Missing path.", nameof(args))),
                (string)(args?[1] ?? throw new ArgumentException("Missing contents.", nameof(args)))));
            return Result<TValue>(default!);
        }

        private static ValueTask<TValue> Result<TValue>(object? value)
        {
            return new ValueTask<TValue>((TValue)value!);
        }
    }
}
