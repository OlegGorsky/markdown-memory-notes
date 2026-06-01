using Notes.Core.Files;
using Notes.Core.Sync;
using Xunit;
using CoreVault = Notes.Core.Vault.Vault;

namespace Notes.Core.Tests.Sync;

public sealed class SyncRepairServiceTests
{
    [Fact]
    public async Task BuildManifestReturnsBoundedContentHashes()
    {
        var fs = new MemoryFileSystem();
        fs.Write("/vault/notes/a.md", "# A");
        fs.Write("/vault/inbox/b.md", "# B");
        fs.Write("/vault/.notes/private.md", "# Private");
        var service = new SyncRepairService(fs);

        var manifest = await service.BuildManifestAsync(new CoreVault("/vault"), maxEntries: 1);

        Assert.True(manifest.Truncated);
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("inbox/b.md", entry.Path);
        Assert.Equal(SyncContentHash.Compute("# B"), entry.Hash);
    }

    [Fact]
    public async Task FindChangesForAsyncReturnsOnlyDifferingAndMissingRemoteFiles()
    {
        var fs = new MemoryFileSystem();
        fs.Write("/vault/notes/changed.md", "# Local");
        fs.Write("/vault/notes/same.md", "# Same");
        fs.Write("/vault/inbox/new.md", "# New");
        var service = new SyncRepairService(fs);
        var remote = new SyncRepairRequest(
            [
                new SyncManifestEntry("notes/changed.md", SyncContentHash.Compute("# Remote")),
                new SyncManifestEntry("notes/same.md", SyncContentHash.Compute("# Same")),
                new SyncManifestEntry("notes/deleted-there.md", SyncContentHash.Compute("# Deleted there"))
            ],
            Truncated: false);

        var changes = await service.FindChangesForAsync(new CoreVault("/vault"), remote, maxChanges: 8);

        Assert.Equal(2, changes.Count);
        Assert.Equal("notes/changed.md", changes[0].Path);
        Assert.Equal("# Local", changes[0].Content);
        Assert.Equal(SyncContentHash.Compute("# Remote"), changes[0].BaseHash);
        Assert.Equal("inbox/new.md", changes[1].Path);
        Assert.Equal("# New", changes[1].Content);
        Assert.Null(changes[1].BaseHash);
    }

    [Fact]
    public async Task FindChangesForAsyncDoesNotSendUnlistedFilesWhenRemoteManifestIsTruncated()
    {
        var fs = new MemoryFileSystem();
        fs.Write("/vault/notes/changed.md", "# Local");
        fs.Write("/vault/inbox/unlisted.md", "# Unlisted");
        var service = new SyncRepairService(fs);
        var remote = new SyncRepairRequest(
            [new SyncManifestEntry("notes/changed.md", SyncContentHash.Compute("# Remote"))],
            Truncated: true);

        var changes = await service.FindChangesForAsync(new CoreVault("/vault"), remote, maxChanges: 8);

        var change = Assert.Single(changes);
        Assert.Equal("notes/changed.md", change.Path);
    }

    [Fact]
    public async Task FindChangesForAsyncBoundsReturnedChanges()
    {
        var fs = new MemoryFileSystem();
        fs.Write("/vault/notes/a.md", "# A local");
        fs.Write("/vault/notes/b.md", "# B local");
        var service = new SyncRepairService(fs);
        var remote = new SyncRepairRequest(
            [
                new SyncManifestEntry("notes/a.md", SyncContentHash.Compute("# A remote")),
                new SyncManifestEntry("notes/b.md", SyncContentHash.Compute("# B remote"))
            ],
            Truncated: false);

        var changes = await service.FindChangesForAsync(new CoreVault("/vault"), remote, maxChanges: 1);

        var change = Assert.Single(changes);
        Assert.Equal("notes/a.md", change.Path);
    }

    private sealed class MemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

        public void Write(string path, string contents)
        {
            files[path] = contents;
        }

        public Task<bool> DirectoryExistsAsync(string path)
        {
            return Task.FromResult(true);
        }

        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(files.ContainsKey(path));
        }

        public Task CreateDirectoryAsync(string path)
        {
            return Task.CompletedTask;
        }

        public Task<string> ReadAllTextAsync(string path)
        {
            return Task.FromResult(files[path]);
        }

        public Task WriteAllTextAsync(string path, string contents)
        {
            files[path] = contents;
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            files.Remove(path);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
        {
            var prefix = path.TrimEnd('/', '\\') + "/";
            return Task.FromResult<IEnumerable<string>>(files.Keys
                .Where(file => file.StartsWith(prefix, StringComparison.Ordinal) &&
                               file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
