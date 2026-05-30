using Notes.Core.Notes;

namespace Notes.Core.Search;

public sealed record SearchResult(Note Note, int Score, IReadOnlyList<string> MatchedTerms);
