using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Core.Vault;

public sealed class VaultService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IFileSystem fileSystem;

    public VaultService(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public async Task<Vault> CreateAsync(string rootPath)
    {
        var vault = new Vault(Path.GetFullPath(rootPath));
        await fileSystem.CreateDirectoryAsync(vault.RootPath);
        await EnsureLayoutAsync(vault);
        return vault;
    }

    public async Task<Vault> OpenAsync(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        if (!await fileSystem.DirectoryExistsAsync(fullPath))
        {
            throw new DirectoryNotFoundException($"Vault directory was not found: {rootPath}");
        }

        var vault = new Vault(fullPath);
        await EnsureLayoutAsync(vault);
        return vault;
    }

    private async Task EnsureLayoutAsync(Vault vault)
    {
        await fileSystem.CreateDirectoryAsync(vault.NotesPath);
        await fileSystem.CreateDirectoryAsync(vault.InboxPath);
        await fileSystem.CreateDirectoryAsync(vault.MetadataPath);

        if (!await fileSystem.FileExistsAsync(vault.SettingsPath))
        {
            var json = JsonSerializer.Serialize(new VaultSettings(1), JsonOptions);
            await fileSystem.WriteAllTextAsync(vault.SettingsPath, json + Environment.NewLine);
        }

        if (!await fileSystem.FileExistsAsync(vault.TrailsPath))
        {
            await fileSystem.WriteAllTextAsync(vault.TrailsPath, "{\n  \"trails\": []\n}" + Environment.NewLine);
        }
    }

    private sealed record VaultSettings(int Version);
}
