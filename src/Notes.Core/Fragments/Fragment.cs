namespace Notes.Core.Fragments;

public sealed record Fragment(string Id, string NoteId, string Name, string Kind, string Text, int StartLine, int EndLine);
