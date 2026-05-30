namespace Notes.Core.Markdown;

public sealed record MarkdownDocument(IReadOnlyDictionary<string, string> Frontmatter, string Body)
{
    public string GetFrontmatterValue(string key, string fallback = "")
    {
        return Frontmatter.TryGetValue(key, out var value) ? value : fallback;
    }
}
