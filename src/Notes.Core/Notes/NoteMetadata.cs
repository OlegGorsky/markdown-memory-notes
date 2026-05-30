namespace Notes.Core.Notes;

public sealed record NoteMetadata(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated);
