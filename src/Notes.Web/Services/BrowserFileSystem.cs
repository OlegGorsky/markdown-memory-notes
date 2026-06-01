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
    public string? CurrentVaultId { get; private set; }
    public string? CurrentVaultName { get; private set; }

    public async Task<BrowserVaultHandle?> TryRestoreVaultAsync(string? vaultId = null)
    {
        var mod = await module.Value;
        var handle = await mod.InvokeAsync<BrowserVaultHandle?>("tryRestoreVault", vaultId);
        if (handle is not null)
        {
            SetCurrentVault(handle);
            IsAvailable = true;
        }
        return handle;
    }

    public async Task<BrowserVaultHandle?> OpenVaultAsync(string? vaultId = null)
    {
        var mod = await module.Value;
        var handle = await mod.InvokeAsync<BrowserVaultHandle?>("openVault", vaultId);
        if (handle is not null)
        {
            SetCurrentVault(handle);
            IsAvailable = true;
        }
        return handle;
    }

    public async Task<BrowserVaultHandle?> CreateVirtualVaultAsync(string? vaultId = null)
    {
        var mod = await module.Value;
        var handle = await mod.InvokeAsync<BrowserVaultHandle?>("createVirtualVault", vaultId);
        if (handle is not null)
        {
            SetCurrentVault(handle);
            IsAvailable = true;
        }
        return handle;
    }

    public async Task<BrowserVaultHandle?> SwitchVaultAsync(string vaultId)
    {
        var mod = await module.Value;
        var handle = await mod.InvokeAsync<BrowserVaultHandle?>("switchVault", vaultId);
        if (handle is not null)
        {
            SetCurrentVault(handle);
            IsAvailable = true;
        }
        return handle;
    }

    private string Rel(string path)
    {
        if (vaultFullPath is null) return path.Replace('\\', '/');
        if (!Path.IsPathFullyQualified(path)) return path.Replace('\\', '/');

        var full = Path.GetFullPath(path).Replace('\\', '/');
        var vaultRoot = vaultFullPath.Replace('\\', '/').TrimEnd('/');
        if (string.Equals(full, vaultRoot, StringComparison.Ordinal))
        {
            return "";
        }

        var vaultPrefix = vaultRoot + "/";
        if (full.StartsWith(vaultPrefix, StringComparison.Ordinal))
        {
            var rel = full[vaultPrefix.Length..].TrimStart('/', '\\');
            return string.IsNullOrEmpty(rel) ? "" : rel;
        }

        return full;
    }

    private void SetCurrentVault(BrowserVaultHandle handle)
    {
        CurrentVaultId = handle.Id;
        CurrentVaultName = handle.Name;
        vaultFullPath = Path.GetFullPath(handle.Path);
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

    public async Task DeleteFileAsync(string path)
    {
        var mod = await module.Value;
        await mod.InvokeVoidAsync("deleteFile", Rel(path));
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

public sealed record BrowserVaultHandle(string Id, string Name, string Path);
