using Notes.Core.Notes;

namespace Notes.Core.Memory;

public sealed record MemoryQuery(Note CurrentNote, string ContextText, int Limit);
