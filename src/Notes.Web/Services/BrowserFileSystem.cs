using Microsoft.JSInterop;
using Notes.Core.Files;

namespace MemoryNotes.Web.Services;

public sealed class BrowserFileSystem : IFileSystem, IAsyncDisposable
{
    private readonly IJSRuntime js;
    private readonly Lazy<Task<IJSObjectReference>> module;
    private string? vaultPath;

    public BrowserFileSystem(IJSRuntime js)
    {
        this.js = js;
        module = new Lazy<Task<IJSObjectReference>>(() =>
            js.InvokeAsync<IJSObjectReference>("import", "./js/file-system-access.js").AsTask());
    }

    public bool IsAvailable { get; private set; }

    public async Task<string?> OpenVaultAsync()
    {
        var mod = await module.Value;
        var name = await mod.InvokeAsync<string?>("openVault");
        if (name is not null)
        {
            vaultPath = name;
            IsAvailable = true;
        }
        return name;
    }

    public async Task<bool> DirectoryExistsAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<bool>("directoryExists", path);
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<bool>("fileExists", path);
    }

    public async Task CreateDirectoryAsync(string path)
    {
        var mod = await module.Value;
        await mod.InvokeVoidAsync("createDirectory", path);
    }

    public async Task<string> ReadAllTextAsync(string path)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<string>("readAllText", path);
    }

    public async Task WriteAllTextAsync(string path, string contents)
    {
        var mod = await module.Value;
        await mod.InvokeVoidAsync("writeAllText", path, contents);
    }

    public async Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        var mod = await module.Value;
        var recurse = searchOption == SearchOption.AllDirectories;
        var results = await mod.InvokeAsync<string[]>("enumerateFiles", path, searchPattern, recurse);
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
