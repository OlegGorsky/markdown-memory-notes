using Notes.Core.Files;
using Notes.Core.Sync;
using Xunit;

namespace Notes.Core.Tests.Sync;

public sealed class SyncChangeApplierTests
{
    private static readonly DateTimeOffset ConflictTime = new(2026, 5, 31, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task ApplyFileWritesWhenTargetDoesNotExist()
    {
        var fs = new MemoryFileSystem();
        var applier = new SyncChangeApplier(fs, () => ConflictTime);

        var result = await applier.ApplyFileAsync("notes/project.md", "# Remote", baseHash: null);

        Assert.Equal(SyncApplyResult.Applied, result);
        Assert.Equal("# Remote", fs.Read("notes/project.md"));
    }

    [Fact]
    public async Task ApplyFileOverwritesWhenBaseHashMatchesCurrentContent()
    {
        var fs = new MemoryFileSystem();
        fs.Write("notes/project.md", "# Local");
        var baseHash = SyncContentHash.Compute("# Local");
        var applier = new SyncChangeApplier(fs, () => ConflictTime);

        var result = await applier.ApplyFileAsync("notes/project.md", "# Remote", baseHash);

        Assert.Equal(SyncApplyResult.Applied, result);
        Assert.Equal("# Remote", fs.Read("notes/project.md"));
    }

    [Fact]
    public async Task ApplyFileKeepsLocalAndSavesConflictWhenBaseHashDiffers()
    {
        var fs = new MemoryFileSystem();
        fs.Write("notes/project.md", "# Local edit");
        var staleBaseHash = SyncContentHash.Compute("# Older");
        var applier = new SyncChangeApplier(fs, () => ConflictTime);

        var result = await applier.ApplyFileAsync("notes/project.md", "# Remote edit", staleBaseHash);

        Assert.Equal(SyncApplyResult.ConflictSaved, result);
        Assert.Equal("# Local edit", fs.Read("notes/project.md"));
        Assert.Equal("# Remote edit", fs.Read("notes/project.conflict-20260531T123456Z.md"));
    }

    [Fact]
    public async Task ApplyDeleteDeletesWhenBaseHashMatchesCurrentContent()
    {
        var fs = new MemoryFileSystem();
        fs.Write("notes/project.md", "# Local");
        var baseHash = SyncContentHash.Compute("# Local");
        var applier = new SyncChangeApplier(fs, () => ConflictTime);

        var result = await applier.ApplyDeleteAsync("notes/project.md", baseHash);

        Assert.Equal(SyncApplyResult.Deleted, result);
        Assert.False(await fs.FileExistsAsync("notes/project.md"));
    }

    [Fact]
    public async Task ApplyDeleteKeepsLocalWhenBaseHashDiffers()
    {
        var fs = new MemoryFileSystem();
        fs.Write("notes/project.md", "# Local edit");
        var staleBaseHash = SyncContentHash.Compute("# Older");
        var applier = new SyncChangeApplier(fs, () => ConflictTime);

        var result = await applier.ApplyDeleteAsync("notes/project.md", staleBaseHash);

        Assert.Equal(SyncApplyResult.ConflictSkipped, result);
        Assert.Equal("# Local edit", fs.Read("notes/project.md"));
    }

    private sealed class MemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

        public string Read(string path)
        {
            return files[path];
        }

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
            return Task.FromResult<IEnumerable<string>>(files.Keys);
        }
    }
}
