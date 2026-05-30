using System.Text;

namespace Notes.Core.Markdown;

public static class MarkdownParser
{
    public static MarkdownDocument Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new MarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return new MarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var frontmatterText = normalized[4..end];
        var body = normalized[(end + 5)..];
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontmatterText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            frontmatter[key] = value;
        }

        return new MarkdownDocument(frontmatter, body);
    }

    public static string Write(IReadOnlyDictionary<string, string> frontmatter, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        foreach (var pair in frontmatter)
        {
            builder.Append(pair.Key).Append(": ").AppendLine(pair.Value);
        }

        builder.AppendLine("---");
        builder.Append(body.TrimStart());
        if (!builder.ToString().EndsWith('\n'))
        {
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
