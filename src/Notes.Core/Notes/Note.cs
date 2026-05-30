namespace Notes.Core.Notes;

public sealed record Note(
    string Id,
    string Title,
    string Path,
    string Body,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public string Excerpt
    {
        get
        {
            var line = Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .FirstOrDefault(static value => value.Length > 0 && !value.StartsWith('#'));
            return line is null ? string.Empty : line.Length <= 140 ? line : line[..140];
        }
    }
}
