using Notes.Core.Files;
using Notes.Core.Fragments;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;

namespace Notes.Desktop.Services;

public sealed class DesktopVaultSession
{
    private readonly PhysicalFileSystem fileSystem = new();
    private readonly InMemorySearchIndex searchIndex = new();

    public DesktopVaultSession(string rootPath)
    {
        Vault = new VaultService(fileSystem).Create(rootPath);
        Notes = new NoteRepository(fileSystem);
        Inbox = new InboxService(fileSystem);
        Trails = new TrailRepository(fileSystem);
        Fragments = new FragmentService();
        QuietMemory = new QuietMemoryService(searchIndex);
        RebuildIndex();
    }

    public Notes.Core.Vault.Vault Vault { get; }
    public NoteRepository Notes { get; }
    public InboxService Inbox { get; }
    public TrailRepository Trails { get; }
    public FragmentService Fragments { get; }
    public QuietMemoryService QuietMemory { get; }

    public IReadOnlyList<Note> RebuildIndex()
    {
        var notes = Notes.List(Vault);
        searchIndex.Rebuild(notes);
        return notes;
    }
}
