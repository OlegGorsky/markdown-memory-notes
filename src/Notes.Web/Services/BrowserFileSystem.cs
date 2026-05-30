using Microsoft.JSInterop;
using Notes.Core.Files;

namespace MemoryNotes.Web.Services;

public sealed class BrowserFileSystem : IFileSystem, IAsyncDisposable
{
    private readonly IJSRuntime js;
    private readonly Lazy<Task<IJSObjectReference>> module;
    private string? vaultFullPath;

    public BrowserFileSystem(IJSRuntime js)
    {
        this.js = js;
        module = new Lazy<Task<IJSObjectReference>>(() =>
            js.InvokeAsync<IJSObjectReference>("import", "./js/file-system-access.js").AsTask());
    }

    public bool IsAvailable { get; private set; }

    public async Task<string?> TryRestoreVaultAsync()
    {
        var mod = await module.Value;
        var name = await mod.InvokeAsync<string?>("tryRestoreVault");
        if (name is not null)
        {
            vaultFullPath = Path.GetFullPath(name);
            IsAvailable = true;
        }
        return name;
    }

    public async Task<string?> OpenVaultAsync()
    {
        var mod = await module.Value;
        var name = await mod.InvokeAsync<string?>("openVault");
        if (name is not null)
        {
            vaultFullPath = Path.GetFullPath(name);
            IsAvailable = true;
        }
        return name;
    }

    private string Rel(string path)
    {
        if (vaultFullPath is null) return path;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(vaultFullPath, StringComparison.Ordinal))
        {
            var rel = full[vaultFullPath.Length..].TrimStart('/', '\\');
            return string.IsNullOrEmpty(rel) ? "." : rel;
        }
        return full;
    }

    public async Task<bool> DirectoryExistsAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<bool>("directoryExists", Rel(path));
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<bool>("fileExists", Rel(path));
    }

    public async Task CreateDirectoryAsync(string path)
    {
        var mod = await module.Value;
        await mod.InvokeVoidAsync("createDirectory", Rel(path));
    }

    public async Task<string> ReadAllTextAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<string>("readAllText", Rel(path));
    }

    public async Task WriteAllTextAsync(string path, string contents)
    {
        var mod = await module.Value;
        await mod.InvokeVoidAsync("writeAllText", Rel(path), contents);
    }

    public async Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        var mod = await module.Value;
        var recurse = searchOption == SearchOption.AllDirectories;
        var results = await mod.InvokeAsync<string[]>("enumerateFiles", Rel(path), searchPattern, recurse);
        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated)
        {
            var mod = await module.Value;
            await mod.DisposeAsync();
        }
    }
}
