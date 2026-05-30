using Notes.Core.Files;
using Notes.Core.Notes;
using Xunit;
using CoreVault = Notes.Core.Vault.Vault;

namespace Notes.Core.Tests.Notes;

public sealed class NoteRepositoryTests
{
    [Fact]
    public void CreateNoteWritesMarkdownWithFrontmatter()
    {
        var vault = CreateVault();
        var repository = new NoteRepository(new PhysicalFileSystem());

        var note = repository.Create(vault, "Local Markdown Notes", "First paragraph.");

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
    public void ListReadsMarkdownNotesFromNotesAndInbox()
    {
        var vault = CreateVault();
        File.WriteAllText(Path.Combine(vault.NotesPath, "alpha.md"), "---\nid: note_alpha\ntitle: Alpha\ncreated: 2026-05-30T10:00:00+03:00\nupdated: 2026-05-30T10:00:00+03:00\n---\n# Alpha\nBody");
        File.WriteAllText(Path.Combine(vault.InboxPath, "2026-05-30.md"), "# Inbox\nCaptured");
        var repository = new NoteRepository(new PhysicalFileSystem());

        var notes = repository.List(vault).OrderBy(note => note.Title).ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal("Alpha", notes[0].Title);
        Assert.Equal("Inbox", notes[1].Title);
    }

    [Fact]
    public void SavePreservesExistingIdAndUpdatesBody()
    {
        var vault = CreateVault();
        var repository = new NoteRepository(new PhysicalFileSystem());
        var created = repository.Create(vault, "Draft", "Original");

        var saved = repository.Save(created with { Body = "Changed body" });

        Assert.Equal(created.Id, saved.Id);
        var text = File.ReadAllText(created.Path);
        Assert.Contains("Changed body", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Original", text, StringComparison.Ordinal);
    }

    private static CoreVault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new global::Notes.Core.Vault.VaultService(new PhysicalFileSystem()).Create(root);
    }
}
