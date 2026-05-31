using System.Globalization;
using Notes.Core.Files;

namespace Notes.Core.Sync;

public sealed class SyncChangeApplier
{
    private readonly IFileSystem fileSystem;
    private readonly Func<DateTimeOffset> now;

    public SyncChangeApplier(IFileSystem fileSystem, Func<DateTimeOffset>? now = null)
    {
        this.fileSystem = fileSystem;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<SyncApplyResult> ApplyFileAsync(string relativePath, string content, string? baseHash)
    {
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var path))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        if (!await fileSystem.FileExistsAsync(path))
        {
            await fileSystem.WriteAllTextAsync(path, content);
            return SyncApplyResult.Applied;
        }

        var current = await fileSystem.ReadAllTextAsync(path);
        var currentHash = SyncContentHash.Compute(current);
        var incomingHash = SyncContentHash.Compute(content);
        if (currentHash == incomingHash)
        {
            return SyncApplyResult.Noop;
        }

        if (baseHash is null || string.Equals(currentHash, baseHash, StringComparison.Ordinal))
        {
            await fileSystem.WriteAllTextAsync(path, content);
            return SyncApplyResult.Applied;
        }

        var conflictPath = await UniqueConflictPathAsync(path);
        await fileSystem.WriteAllTextAsync(conflictPath, content);
        return SyncApplyResult.ConflictSaved;
    }

    public async Task<SyncApplyResult> ApplyDeleteAsync(string relativePath, string? baseHash)
    {
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(relativePath, out var path))
        {
            throw new ArgumentException("Sync path is outside supported Markdown content.", nameof(relativePath));
        }

        if (!await fileSystem.FileExistsAsync(path))
        {
            return SyncApplyResult.Noop;
        }

        if (baseHash is not null)
        {
            var current = await fileSystem.ReadAllTextAsync(path);
            var currentHash = SyncContentHash.Compute(current);
            if (!string.Equals(currentHash, baseHash, StringComparison.Ordinal))
            {
                return SyncApplyResult.ConflictSkipped;
            }
        }

        await fileSystem.DeleteFileAsync(path);
        return SyncApplyResult.Deleted;
    }

    private async Task<string> UniqueConflictPathAsync(string path)
    {
        var suffix = now().ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(path);
        var candidateName = $"{fileName}.conflict-{suffix}.md";
        var candidate = string.IsNullOrEmpty(directory) ? candidateName : $"{directory}/{candidateName}";
        var index = 2;
        while (await fileSystem.FileExistsAsync(candidate))
        {
            candidateName = $"{fileName}.conflict-{suffix}-{index}.md";
            candidate = string.IsNullOrEmpty(directory) ? candidateName : $"{directory}/{candidateName}";
            index++;
        }

        return candidate;
    }
}
