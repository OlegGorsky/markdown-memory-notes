using Microsoft.JSInterop;
using Notes.Core.Files;

namespace MemoryNotes.Web.Services;

/// <summary>
/// IFileSystem implementation using the browser File System Access API.
/// All paths are relative to the vault root directory selected by the user.
/// </summary>
public sealed class BrowserFileSystem : IFileSystem, IAsyncDisposable
{
    private readonly IJSRuntime js;
    private readonly Lazy<Task<IJSObjectReference>> module;
    private string? vaultPath;

    public BrowserFileSystem(IJSRuntime js)
    {
        this.js = js;
        module = new Lazy<Task<IJSObjectReference>>(() =>
            js.InvokeAsync<IJSObjectReference>("import", "/js/file-system-access.js").AsTask());
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

    public bool DirectoryExists(string path)
    {
        return InvokeSync("directoryExists", path);
    }

    public bool FileExists(string path)
    {
        return InvokeSync("fileExists", path);
    }

    public void CreateDirectory(string path)
    {
        Invoke("createDirectory", path);
    }

    public string ReadAllText(string path)
    {
        return Invoke<string>("readAllText", path);
    }

    public void WriteAllText(string path, string contents)
    {
        Invoke("writeAllText", path, contents);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var recurse = searchOption == SearchOption.AllDirectories;
        var results = Invoke<string[]>("enumerateFiles", path, searchPattern, recurse);
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

    // Sync-over-async helpers — Blazor WASM is single-threaded, so this is safe
    private T Invoke<T>(string method, params object?[] args)
    {
        return InvokeAsync<T>(method, args).GetAwaiter().GetResult();
    }

    private void Invoke(string method, params object?[] args)
    {
        InvokeAsync<object>(method, args).GetAwaiter().GetResult();
    }

    private bool InvokeSync(string method, params object?[] args)
    {
        return InvokeAsync<bool>(method, args).GetAwaiter().GetResult();
    }

    private async Task<T> InvokeAsync<T>(string method, params object?[] args)
    {
        var mod = await module.Value;
        return await mod.InvokeAsync<T>(method, args);
    }
}
