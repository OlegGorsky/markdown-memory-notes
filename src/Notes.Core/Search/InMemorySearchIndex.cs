using System.Text.RegularExpressions;
using Notes.Core.Notes;

namespace Notes.Core.Search;

public sealed partial class InMemorySearchIndex : ISearchIndex
{
    private const int MaxQueryTerms = 32;
    private readonly List<Note> notes = new();

    public void Rebuild(IEnumerable<Note> notesToIndex)
    {
        notes.Clear();
        notes.AddRange(notesToIndex);
    }

    public void Upsert(Note note)
    {
        var index = notes.FindIndex(existing => existing.Id == note.Id);
        if (index >= 0)
        {
            notes[index] = note;
            return;
        }

        notes.Add(note);
    }

    public void Remove(string noteId)
    {
        notes.RemoveAll(note => note.Id == noteId);
    }

    public IReadOnlyList<SearchResult> Search(string query, int limit)
    {
        var terms = Tokenize(query).ToArray();
        if (terms.Length == 0 || limit <= 0)
        {
            return Array.Empty<SearchResult>();
        }

        return notes.Select(note => Score(note, terms))
            .Where(static result => result.Score > 0)
            .OrderByDescending(static result => result.Score)
            .ThenByDescending(static result => result.Note.Updated)
            .Take(limit)
            .ToArray();
    }

    private static SearchResult Score(Note note, IReadOnlyList<string> terms)
    {
        var title = note.Title.ToLowerInvariant();
        var body = note.Body.ToLowerInvariant();
        var matched = new List<string>();
        var score = 0;

        foreach (var term in terms)
        {
            var termScore = 0;
            if (title.Contains(term, StringComparison.Ordinal))
            {
                termScore += 5;
            }

            if (body.Contains(term, StringComparison.Ordinal))
            {
                termScore += 2;
            }

            if (termScore > 0)
            {
                matched.Add(term);
                score += termScore;
            }
        }

        return new SearchResult(note, score, matched);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in WordRegex().Matches(value))
        {
            if (match.Value.Length < 3)
            {
                continue;
            }

            var term = match.Value.ToLowerInvariant();
            if (!seen.Add(term))
            {
                continue;
            }

            yield return term;
            if (seen.Count >= MaxQueryTerms)
            {
                yield break;
            }
        }
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
