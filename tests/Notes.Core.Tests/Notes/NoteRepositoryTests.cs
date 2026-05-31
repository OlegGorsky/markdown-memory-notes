using Notes.Core.Files;
using Notes.Core.Notes;
using Xunit;
using CoreVault = Notes.Core.Vault.Vault;

namespace Notes.Core.Tests.Notes;

public sealed class NoteRepositoryTests
{
    [Fact]
    public async Task CreateNoteWritesMarkdownWithFrontmatter()
    {
        var vault = await CreateVaultAsync();
        var repository = new NoteRepository(new PhysicalFileSystem());

        var note = await repository.CreateAsync(vault, "Local Markdown Notes", "First paragraph.");

        Assert.StartsWith("note_", note.Id, StringComparison.Ordinal);
        Assert.Equal("Local Markdown Notes", note.Title);
        Assert.EndsWith("local-markdown-notes.md", note.Path, StringComparison.Ordinal);
        var fileText = File.ReadAllText(note.Path);
        Assert.Contains("id: ", fileText, StringComparison.Ordinal);
        Assert.Contains("title: Local Markdown Notes", fileText, StringComparison.Ordinal);
        Assert.Contains("# Local Markdown Notes", fileText, StringComparison.Ordinal);
        Assert.Contains("First paragraph.", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListReadsMarkdownNotesFromNotesAndInbox()
    {
        var vault = await CreateVaultAsync();
        File.WriteAllText(Path.Combine(vault.NotesPath, "alpha.md"), "---\nid: note_alpha\ntitle: Alpha\ncreated: 2026-05-30T10:00:00+03:00\nupdated: 2026-05-30T10:00:00+03:00\n---\n# Alpha\nBody");
        File.WriteAllText(Path.Combine(vault.InboxPath, "2026-05-30.md"), "# Inbox\nCaptured");
        var repository = new NoteRepository(new PhysicalFileSystem());

        var notes = (await repository.ListAsync(vault)).OrderBy(note => note.Title).ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal("Alpha", notes[0].Title);
        Assert.Equal("Inbox", notes[1].Title);
    }

    [Fact]
    public async Task ReadUsesStablePathIdWhenFrontmatterIsMissing()
    {
        var vault = await CreateVaultAsync();
        var path = Path.Combine(vault.InboxPath, "imported.md");
        var otherPath = Path.Combine(vault.InboxPath, "other.md");
        File.WriteAllText(path, "# Imported\nCaptured elsewhere");
        File.WriteAllText(otherPath, "# Other\nCaptured elsewhere");
        var repository = new NoteRepository(new PhysicalFileSystem());

        var firstRead = await repository.ReadAsync(path);
        var secondRead = await repository.ReadAsync(path);
        var otherNote = await repository.ReadAsync(otherPath);

        Assert.StartsWith("path_", firstRead.Id, StringComparison.Ordinal);
        Assert.Equal(firstRead.Id, secondRead.Id);
        Assert.NotEqual(firstRead.Id, otherNote.Id);
    }

    [Fact]
    public async Task ListUsesVaultRelativePathIdWhenFrontmatterIsMissing()
    {
        var firstVault = await CreateVaultAsync();
        var secondVault = await CreateVaultAsync();
        File.WriteAllText(Path.Combine(firstVault.NotesPath, "imported.md"), "# Imported\nCaptured elsewhere");
        File.WriteAllText(Path.Combine(secondVault.NotesPath, "imported.md"), "# Imported\nCaptured elsewhere");
        var repository = new NoteRepository(new PhysicalFileSystem());

        var firstNote = Assert.Single(await repository.ListAsync(firstVault));
        var secondNote = Assert.Single(await repository.ListAsync(secondVault));

        Assert.StartsWith("path_", firstNote.Id, StringComparison.Ordinal);
        Assert.Equal(firstNote.Id, secondNote.Id);
    }

    [Fact]
    public async Task SavePreservesExistingIdAndUpdatesBody()
    {
        var vault = await CreateVaultAsync();
        var repository = new NoteRepository(new PhysicalFileSystem());
        var created = await repository.CreateAsync(vault, "Draft", "Original");

        var saved = await repository.SaveAsync(created with { Body = "Changed body" });

        Assert.Equal(created.Id, saved.Id);
        var text = File.ReadAllText(created.Path);
        Assert.Contains("Changed body", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Original", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteRemovesMarkdownFileFromVault()
    {
        var vault = await CreateVaultAsync();
        var repository = new NoteRepository(new PhysicalFileSystem());
        var created = await repository.CreateAsync(vault, "Disposable", "Remove me");

        await repository.DeleteAsync(created);

        Assert.False(File.Exists(created.Path));
        Assert.DoesNotContain((await repository.ListAsync(vault)), note => note.Id == created.Id);
    }

    [Fact]
    public async Task ListReadsMarkdownFilesWithBoundedConcurrency()
    {
        var fileSystem = new ConcurrentReadFileSystem(fileCount: 24);
        var repository = new NoteRepository(fileSystem);

        var notes = await repository.ListAsync(new CoreVault("/vault"));

        Assert.Equal(24, notes.Count);
        Assert.InRange(fileSystem.MaxConcurrentReads, 2, 8);
    }

    private static Task<CoreVault> CreateVaultAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new global::Notes.Core.Vault.VaultService(new PhysicalFileSystem()).CreateAsync(root);
    }

    private sealed class ConcurrentReadFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        private int activeReads;
        private int maxConcurrentReads;

        public ConcurrentReadFileSystem(int fileCount)
        {
            for (var index = 0; index < fileCount; index++)
            {
                var path = $"/vault/notes/note-{index:D2}.md";
                files[path] = $"""
                    ---
                    id: note_{index:D2}
                    title: Note {index:D2}
                    created: 2026-05-31T10:00:00+03:00
                    updated: 2026-05-31T10:00:00+03:00
                    ---
                    # Note {index:D2}
                    Body {index:D2}
                    """;
            }
        }

        public int MaxConcurrentReads => Volatile.Read(ref maxConcurrentReads);

        public Task<bool> DirectoryExistsAsync(string path)
        {
            return Task.FromResult(path is "/vault/notes" or "/vault/inbox");
        }

        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(files.ContainsKey(path));
        }

        public Task CreateDirectoryAsync(string path)
        {
            return Task.CompletedTask;
        }

        public async Task<string> ReadAllTextAsync(string path)
        {
            var active = Interlocked.Increment(ref activeReads);
            UpdateMaxConcurrentReads(active);
            try
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
                return files[path];
            }
            finally
            {
                Interlocked.Decrement(ref activeReads);
            }
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
            IEnumerable<string> result = path == "/vault/notes"
                ? files.Keys
                : Enumerable.Empty<string>();
            return Task.FromResult<IEnumerable<string>>(result);
        }

        private void UpdateMaxConcurrentReads(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref maxConcurrentReads);
                if (active <= current ||
                    Interlocked.CompareExchange(ref maxConcurrentReads, active, current) == current)
                {
                    return;
                }
            }
        }
    }
}
