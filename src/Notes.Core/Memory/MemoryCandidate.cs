using Notes.Core.Notes;

namespace Notes.Core.Memory;

public sealed record MemoryCandidate(Note Note, string Kind, string Reason, int Score);
