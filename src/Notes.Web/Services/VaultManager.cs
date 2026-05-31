using Microsoft.JSInterop;

namespace MemoryNotes.Web.Services;

public sealed class VaultManager : IAsyncDisposable
{
    private readonly IJSRuntime js;
    private IJSObjectReference? _module;

    public VaultManager(IJSRuntime js)
    {
        this.js = js;
    }

    private async Task<IJSObjectReference> ModuleAsync()
    {
        return _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/vault-store.js");
    }

    public async Task<List<VaultEntry>> ListAsync()
    {
        var mod = await ModuleAsync();
        var items = await mod.InvokeAsync<VaultEntry[]>("listVaults");
        return items?.ToList() ?? [];
    }

    public async Task SaveAsync(VaultEntry vault)
    {
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("saveVault", vault);
    }

    public async Task DeleteAsync(string id)
    {
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("deleteVault", id);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }
}

public sealed record VaultEntry(string Id, string Name, string Path, string? SyncCode);
