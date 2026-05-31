namespace Notes.Core.Files;

public static class VaultRelativePath
{
    private static readonly StringComparer SegmentComparer = StringComparer.Ordinal;
    private static readonly string[] AllowedRoots = ["notes", "inbox"];

    public static bool TryNormalizeMarkdownContentPath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var value = path.Replace('\\', '/');
        if (value.StartsWith("/", StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Any(static character => char.IsControl(character)))
        {
            return false;
        }

        var segments = value.Split('/');
        if (segments.Length < 2)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) ||
                segment.Equals(".", StringComparison.Ordinal) ||
                segment.Equals("..", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!AllowedRoots.Contains(segments[0], SegmentComparer))
        {
            return false;
        }

        if (!segments[^1].EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }
}
