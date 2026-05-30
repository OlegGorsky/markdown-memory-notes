using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Core.Files;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;

namespace Notes.Mobile.ViewModels;

public sealed partial class MobileMainViewModel : ObservableObject
{
    private readonly InMemorySearchIndex searchIndex = new();
    private readonly PhysicalFileSystem fileSystem = new();
    private readonly VaultService vaultService;
    private readonly NoteRepository noteRepository;
    private readonly InboxService inboxService;
    private readonly TrailRepository trailRepository;
    private readonly QuietMemoryService quietMemory;

    [ObservableProperty]
    private string vaultPath;

    [ObservableProperty]
    private string captureText = string.Empty;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string status = string.Empty;

    [ObservableProperty]
    private Note? selectedNote;

    [ObservableProperty]
    private string noteBody = string.Empty;

    public MobileMainViewModel()
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "MarkdownMemoryNotesVault");
        vaultPath = defaultPath;
        vaultService = new VaultService(fileSystem);
        noteRepository = new NoteRepository(fileSystem);
        inboxService = new InboxService(fileSystem);
        trailRepository = new TrailRepository(fileSystem);
        quietMemory = new QuietMemoryService(searchIndex);

        Notes = new ObservableCollection<Note>();
        QuietMemoryItems = new ObservableCollection<string>();
        Trails = new ObservableCollection<string>();

        try
        {
            vaultService.Open(defaultPath);
            ReloadNotes();
            Status = "Ready";
        }
        catch
        {
            vaultService.Create(defaultPath);
            ReloadNotes();
            Status = "Vault created";
        }
    }

    public ObservableCollection<Note> Notes { get; }
    public ObservableCollection<string> QuietMemoryItems { get; }
    public ObservableCollection<string> Trails { get; }

    [RelayCommand]
    private void Capture()
    {
        if (string.IsNullOrWhiteSpace(CaptureText)) return;

        var vault = new Notes.Core.Vault.Vault(VaultPath);
        inboxService.Capture(vault, CaptureText);
        CaptureText = string.Empty;
        ReloadNotes();
        Status = "Captured";
    }

    [RelayCommand]
    private void NewNote()
    {
        var vault = new Notes.Core.Vault.Vault(VaultPath);
        var note = noteRepository.Create(vault, "Untitled", "Start writing...");
        ReloadNotes();
        SelectedNote = note;
        NoteBody = note.Body;
        Status = "New note";
    }

    [RelayCommand]
    private void SaveNote()
    {
        if (SelectedNote is null) return;

        var saved = noteRepository.Save(SelectedNote with { Body = NoteBody });
        SelectedNote = saved;
        ReloadNotes();
        Status = "Saved";
    }

    [RelayCommand]
    private void SearchNotes()
    {
        var vault = new Notes.Core.Vault.Vault(VaultPath);
        var allNotes = noteRepository.List(vault);
        searchIndex.Rebuild(allNotes);
        var results = searchIndex.Search(SearchQuery, 20);

        Notes.Clear();
        foreach (var result in results)
        {
            Notes.Add(result.Note);
        }

        RefreshQuietMemory();
        Status = $"{Notes.Count} notes";
    }

    private void ReloadNotes()
    {
        var vault = new Notes.Core.Vault.Vault(VaultPath);
        var notes = noteRepository.List(vault);
        searchIndex.Rebuild(notes);

        Notes.Clear();
        foreach (var note in notes)
        {
            Notes.Add(note);
        }

        RefreshQuietMemory();
        RefreshTrails();
        Status = $"{Notes.Count} notes";
    }

    private void RefreshQuietMemory()
    {
        QuietMemoryItems.Clear();
        if (SelectedNote is null) return;

        foreach (var candidate in quietMemory.Suggest(
            new MemoryQuery(SelectedNote, SelectedNote.Body, 5)))
        {
            QuietMemoryItems.Add(candidate.Note.Title);
        }
    }

    private void RefreshTrails()
    {
        Trails.Clear();
        var vault = new Notes.Core.Vault.Vault(VaultPath);
        foreach (var trail in trailRepository.List(vault))
        {
            Trails.Add($"{trail.Title} ({trail.Items.Count})");
        }
    }

    partial void OnSelectedNoteChanged(Note? value)
    {
        if (value is not null)
        {
            NoteBody = value.Body;
            RefreshQuietMemory();
        }
    }
}
