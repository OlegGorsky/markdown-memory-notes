using Notes.Core.Notes;

namespace Notes.Core.Search;

public interface ISearchIndex
{
    void Rebuild(IEnumerable<Note> notes);
    IReadOnlyList<SearchResult> Search(string query, int limit);
}
