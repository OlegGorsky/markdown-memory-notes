using Notes.Core.Search;

namespace Notes.Core.Memory;

public sealed class QuietMemoryService
{
    private const int MaxContextCharacters = 4096;
    private readonly ISearchIndex searchIndex;

    public QuietMemoryService(ISearchIndex searchIndex)
    {
        this.searchIndex = searchIndex;
    }

    public IReadOnlyList<MemoryCandidate> Suggest(MemoryQuery query)
    {
        var context = string.IsNullOrWhiteSpace(query.ContextText)
            ? BoundFallbackContext(query.CurrentNote.Title, query.CurrentNote.Body)
            : BoundContext(query.ContextText);

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

    private static string BoundFallbackContext(string title, string body)
    {
        var prefix = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim() + " ";
        var remaining = Math.Max(0, MaxContextCharacters - prefix.Length);
        var boundedBody = body.Length > remaining ? body[^remaining..] : body;
        return prefix.Length + boundedBody.Length > MaxContextCharacters
            ? (prefix + boundedBody)[..MaxContextCharacters]
            : prefix + boundedBody;
    }

    private static string BoundContext(string context)
    {
        if (context.Length > MaxContextCharacters)
        {
            context = context[^MaxContextCharacters..];
        }

        return context;
    }
}
