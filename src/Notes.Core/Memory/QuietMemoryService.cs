using Notes.Core.Search;

namespace Notes.Core.Memory;

public sealed class QuietMemoryService
{
    private readonly ISearchIndex searchIndex;

    public QuietMemoryService(ISearchIndex searchIndex)
    {
        this.searchIndex = searchIndex;
    }

    public IReadOnlyList<MemoryCandidate> Suggest(MemoryQuery query)
    {
        var context = string.IsNullOrWhiteSpace(query.ContextText)
            ? query.CurrentNote.Title + " " + query.CurrentNote.Body
            : query.ContextText;

        return searchIndex.Search(context, query.Limit + 1)
            .Where(result => result.Note.Id != query.CurrentNote.Id)
            .Take(query.Limit)
            .Select(static result => new MemoryCandidate(
                result.Note,
                "Related note",
                "Matched " + string.Join(", ", result.MatchedTerms),
                result.Score))
            .ToArray();
    }
}
