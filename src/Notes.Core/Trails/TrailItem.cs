namespace Notes.Core.Trails;

public sealed record TrailItem(string Kind, string NoteId, string? FragmentId)
{
    public static TrailItem Note(string noteId) => new("note", noteId, null);

    public static TrailItem Fragment(string noteId, string fragmentId) => new("fragment", noteId, fragmentId);
}
