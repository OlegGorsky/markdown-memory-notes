using Notes.Core.Notes;

namespace Notes.Core.Fragments;

public sealed class FragmentService
{
    public IReadOnlyList<Fragment> GetFragments(Note note)
    {
        return FragmentParser.Parse(note.Id, note.Body);
    }

    public Note MarkFragment(Note note, string selectedText, string name)
    {
        var fragmentId = "frag_" + Guid.NewGuid().ToString("N");
        var markedBody = FragmentMarker.Mark(note.Body, selectedText, name, fragmentId);
        return note with { Body = markedBody };
    }
}
