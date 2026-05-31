using System.Text.RegularExpressions;
using Notes.Core.Notes;

namespace Notes.Core.Search;

public sealed partial class InMemorySearchIndex : ISearchIndex
{
    private const int MaxQueryTerms = 32;
    private readonly Dictionary<string, IndexedNote> notesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> noteIdsByTerm = new(StringComparer.Ordinal);

    public void Rebuild(IEnumerable<Note> notesToIndex)
    {
        notesById.Clear();
        noteIdsByTerm.Clear();
        foreach (var note in notesToIndex)
        {
            Upsert(note);
        }
    }

    public void Upsert(Note note)
    {
        Remove(note.Id);

        var indexed = IndexedNote.Create(note);
        notesById[note.Id] = indexed;
        foreach (var term in indexed.Terms)
        {
            if (!noteIdsByTerm.TryGetValue(term, out var noteIds))
            {
                noteIds = new HashSet<string>(StringComparer.Ordinal);
                noteIdsByTerm[term] = noteIds;
            }

            noteIds.Add(note.Id);
        }
    }

    public void Remove(string noteId)
    {
        if (!notesById.Remove(noteId, out var indexed))
        {
            return;
        }

        foreach (var term in indexed.Terms)
        {
            if (!noteIdsByTerm.TryGetValue(term, out var noteIds))
            {
                continue;
            }

            noteIds.Remove(noteId);
            if (noteIds.Count == 0)
            {
                noteIdsByTerm.Remove(term);
            }
        }
    }

    public IReadOnlyList<SearchResult> Search(string query, int limit)
    {
        var terms = Tokenize(query).ToArray();
        if (terms.Length == 0 || limit <= 0)
        {
            return Array.Empty<SearchResult>();
        }

        return CandidateNoteIds(terms)
            .Select(noteId => Score(notesById[noteId], terms))
            .Where(static result => result.Score > 0)
            .OrderByDescending(static result => result.Score)
            .ThenByDescending(static result => result.Note.Updated)
            .Take(limit)
            .ToArray();
    }

    private IEnumerable<string> CandidateNoteIds(IReadOnlyList<string> terms)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (!noteIdsByTerm.TryGetValue(term, out var noteIds))
            {
                continue;
            }

            foreach (var noteId in noteIds)
            {
                if (seen.Add(noteId))
                {
                    yield return noteId;
                }
            }
        }
    }

    private static SearchResult Score(IndexedNote indexed, IReadOnlyList<string> terms)
    {
        var matched = new List<string>();
        var score = 0;

        foreach (var term in terms)
        {
            var termScore = 0;
            if (indexed.TitleTerms.Contains(term))
            {
                termScore += 5;
            }

            if (indexed.BodyTerms.Contains(term))
            {
                termScore += 2;
            }

            if (termScore > 0)
            {
                matched.Add(term);
                score += termScore;
            }
        }

        return new SearchResult(indexed.Note, score, matched);
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

    private sealed class IndexedNote
    {
        private IndexedNote(Note note, HashSet<string> titleTerms, HashSet<string> bodyTerms)
        {
            Note = note;
            TitleTerms = titleTerms;
            BodyTerms = bodyTerms;
            Terms = titleTerms.Concat(bodyTerms).ToArray();
        }

        public Note Note { get; }
        public HashSet<string> TitleTerms { get; }
        public HashSet<string> BodyTerms { get; }
        public IReadOnlyList<string> Terms { get; }

        public static IndexedNote Create(Note note)
        {
            return new IndexedNote(note, TokenSet(note.Title), TokenSet(note.Body));
        }

        private static HashSet<string> TokenSet(string value)
        {
            var terms = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in WordRegex().Matches(value))
            {
                if (match.Value.Length >= 3)
                {
                    terms.Add(match.Value.ToLowerInvariant());
                }
            }

            return terms;
        }
    }
}
