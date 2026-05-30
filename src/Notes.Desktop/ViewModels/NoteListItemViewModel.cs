using Notes.Core.Notes;

namespace Notes.Desktop.ViewModels;

public sealed class NoteListItemViewModel
{
    public NoteListItemViewModel(Note note)
    {
        Note = note;
    }

    public Note Note { get; }
    public string Title => Note.Title;
    public string Excerpt => Note.Excerpt;
    public string Updated => Note.Updated == DateTimeOffset.MinValue ? "Unknown" : Note.Updated.ToString("yyyy-MM-dd HH:mm");
}
