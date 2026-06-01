using Notes.Core.Files;

namespace Notes.Core.Sync;

public sealed record SyncRepairFileChange(string Path, string Content, string? BaseHash);

public sealed class SyncRepairService
{
    private const int MaxConcurrentReads = 8;
    private readonly IFileSystem fileSystem;

    public SyncRepairService(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public async Task<SyncRepairManifest> BuildManifestAsync(global::Notes.Core.Vault.Vault vault, int maxEntries)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);

        var files = await EnumerateContentFilesAsync(vault);
        var selected = files.Take(maxEntries).ToArray();
        var entries = new SyncManifestEntry[selected.Length];

        await Parallel.ForAsync(
            0,
            selected.Length,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentReads },
            async (index, _) =>
            {
                var content = await fileSystem.ReadAllTextAsync(selected[index].FileSystemPath);
                entries[index] = new SyncManifestEntry(selected[index].RelativePath, SyncContentHash.Compute(content));
            });

        return new SyncRepairManifest(entries, files.Count > selected.Length);
    }

    public async Task<IReadOnlyList<SyncRepairFileChange>> FindChangesForAsync(
        global::Notes.Core.Vault.Vault vault,
        SyncRepairRequest remote,
        int maxChanges)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChanges);

        var changes = new List<SyncRepairFileChange>();
        var remoteEntries = NormalizeRemoteEntries(remote.Entries);
        foreach (var entry in remoteEntries.Values.OrderBy(static entry => entry.Path, StringComparer.Ordinal))
        {
            if (changes.Count >= maxChanges)
            {
                return changes;
            }

            var localPath = LocalPath(vault, entry.Path);
            if (!await fileSystem.FileExistsAsync(localPath))
            {
                continue;
            }

            var content = await fileSystem.ReadAllTextAsync(localPath);
            var localHash = SyncContentHash.Compute(content);
            if (!string.Equals(localHash, entry.Hash, StringComparison.Ordinal))
            {
                changes.Add(new SyncRepairFileChange(entry.Path, content, entry.Hash));
            }
        }

        if (remote.Truncated || changes.Count >= maxChanges)
        {
            return changes;
        }

        var localFiles = await EnumerateContentFilesAsync(vault);
        foreach (var localFile in localFiles)
        {
            if (changes.Count >= maxChanges)
            {
                break;
            }

            if (remoteEntries.ContainsKey(localFile.RelativePath))
            {
                continue;
            }

            var content = await fileSystem.ReadAllTextAsync(localFile.FileSystemPath);
            changes.Add(new SyncRepairFileChange(localFile.RelativePath, content, BaseHash: null));
        }

        return changes;
    }

    private static Dictionary<string, SyncManifestEntry> NormalizeRemoteEntries(
        IReadOnlyList<SyncManifestEntry> entries)
    {
        var normalized = new Dictionary<string, SyncManifestEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!VaultRelativePath.TryNormalizeMarkdownContentPath(entry.Path, out var path) ||
                !SyncContentHash.IsValid(entry.Hash) ||
                normalized.ContainsKey(path))
            {
                continue;
            }

            normalized[path] = new SyncManifestEntry(path, entry.Hash);
        }

        return normalized;
    }

    private async Task<List<ContentFile>> EnumerateContentFilesAsync(global::Notes.Core.Vault.Vault vault)
    {
        var files = new List<ContentFile>();
        await AddContentFilesAsync(vault, vault.NotesPath, files);
        await AddContentFilesAsync(vault, vault.InboxPath, files);
        return files
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private async Task AddContentFilesAsync(
        global::Notes.Core.Vault.Vault vault,
        string directory,
        List<ContentFile> files)
    {
        if (!await fileSystem.DirectoryExistsAsync(directory))
        {
            return;
        }

        foreach (var file in await fileSystem.EnumerateFilesAsync(directory, "*.md", SearchOption.AllDirectories))
        {
            if (TryGetVaultRelativePath(vault, file, out var relativePath))
            {
                files.Add(new ContentFile(relativePath, file));
            }
        }
    }

    private static bool TryGetVaultRelativePath(
        global::Notes.Core.Vault.Vault vault,
        string fileSystemPath,
        out string relativePath)
    {
        var normalizedPath = fileSystemPath.Replace('\\', '/');
        var normalizedRoot = Path.GetFullPath(vault.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
        var rootPrefix = normalizedRoot + "/";
        var candidate = normalizedPath.StartsWith(rootPrefix, StringComparison.Ordinal)
            ? normalizedPath[rootPrefix.Length..]
            : normalizedPath.TrimStart('/', '\\');

        return VaultRelativePath.TryNormalizeMarkdownContentPath(candidate, out relativePath);
    }

    private static string LocalPath(global::Notes.Core.Vault.Vault vault, string relativePath)
    {
        return Path.Combine(vault.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record ContentFile(string RelativePath, string FileSystemPath);
}
