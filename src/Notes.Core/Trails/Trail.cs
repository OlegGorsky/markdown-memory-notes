namespace Notes.Core.Trails;

public sealed record Trail(
    string Id,
    string Title,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    IReadOnlyList<TrailItem> Items);
