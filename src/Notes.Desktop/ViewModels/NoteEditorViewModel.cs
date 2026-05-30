using CommunityToolkit.Mvvm.ComponentModel;
using Notes.Core.Notes;
using Notes.Desktop.Services;

namespace Notes.Desktop.ViewModels;

public sealed partial class NoteEditorViewModel : ObservableObject
{
    private readonly DesktopVaultSession session;

    [ObservableProperty]
    private Note? note;

    [ObservableProperty]
    private string markdown = string.Empty;

    [ObservableProperty]
    private string previewHtml = string.Empty;

    [ObservableProperty]
    private string saveState = "Saved";

    public NoteEditorViewModel(DesktopVaultSession session)
    {
        this.session = session;
    }

    public void Load(Note selectedNote)
    {
        Note = selectedNote;
        Markdown = selectedNote.Body;
        PreviewHtml = global::Markdig.Markdown.ToHtml(Markdown);
        SaveState = "Saved";
    }

    partial void OnMarkdownChanged(string value)
    {
        PreviewHtml = global::Markdig.Markdown.ToHtml(value);
        SaveState = Note is null ? "No note" : "Unsaved";
    }

    public Note? Save()
    {
        if (Note is null)
        {
            return null;
        }

        var saved = session.Notes.SaveAsync(Note with { Body = Markdown }).GetAwaiter().GetResult();
        Note = saved;
        SaveState = "Saved";
        session.RebuildIndex();
        return saved;
    }
}
