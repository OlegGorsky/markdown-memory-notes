using Notes.Core.Fragments;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;

namespace MemoryNotes.Web.Services;

public sealed class WebVaultSession
{
    private readonly BrowserFileSystem fileSystem;
    private readonly InMemorySearchIndex searchIndex = new();

    public WebVaultSession(BrowserFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
        VaultService = new VaultService(fileSystem);
        Notes = new NoteRepository(fileSystem);
        Inbox = new InboxService(fileSystem);
        Trails = new TrailRepository(fileSystem);
        Fragments = new FragmentService();
        QuietMemory = new QuietMemoryService(searchIndex);
    }

    public VaultService VaultService { get; }
    public NoteRepository Notes { get; }
    public InboxService Inbox { get; }
    public TrailRepository Trails { get; }
    public FragmentService Fragments { get; }
    public QuietMemoryService QuietMemory { get; }

    public Vault? CurrentVault { get; private set; }
    public bool IsOpen => CurrentVault is not null;

    public async Task<Vault> OpenOrCreateAsync(string path)
    {
        CurrentVault = await fileSystem.DirectoryExistsAsync(path)
            ? await VaultService.OpenAsync(path)
            : await VaultService.CreateAsync(path);
        await RebuildIndexAsync();
        return CurrentVault;
    }

    public async Task<IReadOnlyList<Note>> RebuildIndexAsync()
    {
        if (CurrentVault is null) return Array.Empty<Note>();
        var notes = await Notes.ListAsync(CurrentVault);
        searchIndex.Rebuild(notes);
        return notes;
    }

    public IReadOnlyList<SearchResult> Search(string query, int limit = 20)
    {
        return searchIndex.Search(query, limit);
    }
}
