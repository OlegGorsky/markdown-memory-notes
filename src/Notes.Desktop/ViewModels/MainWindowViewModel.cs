using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Trails;
using Notes.Desktop.Models;
using Notes.Desktop.Services;

namespace Notes.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly DesktopVaultSession session;

    [ObservableProperty]
    private NoteListItemViewModel? selectedNote;

    [ObservableProperty]
    private string captureText = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string status = "Ready";

    public MainWindowViewModel()
    {
        var demoVault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MarkdownMemoryNotesVault");
        session = new DesktopVaultSession(demoVault);
        Editor = new NoteEditorViewModel(session);
        Navigation = new ObservableCollection<NavigationItem>
        {
            new("inbox", "Inbox"),
            new("notes", "Notes"),
            new("trails", "Trails"),
            new("fragments", "Fragments"),
            new("search", "Search"),
            new("settings", "Settings")
        };
        Notes = new ObservableCollection<NoteListItemViewModel>();
        QuietMemory = new ObservableCollection<QuietMemoryItemViewModel>();
        Trails = new ObservableCollection<TrailViewModel>();
        Reload();
    }

    public string VaultName => session.Vault.RootPath;
    public ObservableCollection<NavigationItem> Navigation { get; }
    public ObservableCollection<NoteListItemViewModel> Notes { get; }
    public ObservableCollection<QuietMemoryItemViewModel> QuietMemory { get; }
    public ObservableCollection<TrailViewModel> Trails { get; }
    public NoteEditorViewModel Editor { get; }

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        Editor.Load(value.Note);
        RefreshQuietMemory(value.Note, value.Note.Title + " " + value.Note.Body);
    }

    [RelayCommand]
    private async Task NewNoteAsync()
    {
        var note = await session.Notes.CreateAsync(session.Vault, "Untitled note", "Start writing here.");
        Reload();
        SelectedNote = Notes.FirstOrDefault(item => item.Note.Id == note.Id);
        Status = "Note created";
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        var saved = Editor.Save();
        if (saved is not null)
        {
            Reload();
            SelectedNote = Notes.FirstOrDefault(item => item.Note.Id == saved.Id);
            Status = "Saved";
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(CaptureText))
        {
            Status = "Capture is empty";
            return;
        }

        await session.Inbox.CaptureAsync(session.Vault, CaptureText);
        CaptureText = string.Empty;
        Reload();
        Status = "Captured";
    }

    [RelayCommand]
    private void Search()
    {
        Notes.Clear();
        var allNotes = session.RebuildIndex();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? allNotes
            : allNotes.Where(note => note.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || note.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var note in filtered)
        {
            Notes.Add(new NoteListItemViewModel(note));
        }

        Status = $"{Notes.Count} notes";
    }

    [RelayCommand]
    private async Task CreateTrailAsync()
    {
        var trail = await session.Trails.CreateAsync(session.Vault, "New thought trail");
        if (SelectedNote is not null)
        {
            await session.Trails.AddItemAsync(session.Vault, trail.Id, TrailItem.Note(SelectedNote.Note.Id));
        }

        ReloadTrails();
        Status = "Trail created";
    }

    private void Reload()
    {
        Notes.Clear();
        foreach (var note in session.RebuildIndex())
        {
            Notes.Add(new NoteListItemViewModel(note));
        }

        ReloadTrails();
        Status = $"{Notes.Count} notes";
    }

    private void ReloadTrails()
    {
        Trails.Clear();
        foreach (var trail in session.Trails.ListAsync(session.Vault).GetAwaiter().GetResult())
        {
            Trails.Add(new TrailViewModel(trail));
        }
    }

    private void RefreshQuietMemory(Note note, string context)
    {
        QuietMemory.Clear();
        foreach (var candidate in session.QuietMemory.Suggest(new MemoryQuery(note, context, 5)))
        {
            QuietMemory.Add(new QuietMemoryItemViewModel(candidate));
        }
    }
}
