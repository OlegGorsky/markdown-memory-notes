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

    public Vault Create(string rootPath)
    {
        var vault = new Vault(Path.GetFullPath(rootPath));
        fileSystem.CreateDirectory(vault.RootPath);
        EnsureLayout(vault);
        return vault;
    }

    public Vault Open(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        if (!fileSystem.DirectoryExists(fullPath))
        {
            throw new DirectoryNotFoundException($"Vault directory was not found: {rootPath}");
        }

        var vault = new Vault(fullPath);
        EnsureLayout(vault);
        return vault;
    }

    private void EnsureLayout(Vault vault)
    {
        fileSystem.CreateDirectory(vault.NotesPath);
        fileSystem.CreateDirectory(vault.InboxPath);
        fileSystem.CreateDirectory(vault.MetadataPath);

        if (!fileSystem.FileExists(vault.SettingsPath))
        {
            var json = JsonSerializer.Serialize(new VaultSettings(1), JsonOptions);
            fileSystem.WriteAllText(vault.SettingsPath, json + Environment.NewLine);
        }

        if (!fileSystem.FileExists(vault.TrailsPath))
        {
            fileSystem.WriteAllText(vault.TrailsPath, "{\n  \"trails\": []\n}" + Environment.NewLine);
        }
    }

    private sealed record VaultSettings(int Version);
}
