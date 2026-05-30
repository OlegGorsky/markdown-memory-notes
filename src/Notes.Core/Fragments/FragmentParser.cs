using System.Text.RegularExpressions;
using Notes.Core.Notes;

namespace Notes.Core.Fragments;

public static partial class FragmentParser
{
    public static IReadOnlyList<Fragment> Parse(string noteId, string markdown)
    {
        var fragments = new List<Fragment>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var name = line.TrimStart('#').Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fragments.Add(new Fragment($"{noteId}#{NoteTitle.ToSlug(name)}", noteId, name, "heading", line, index + 1, index + 1));
                }
            }
        }

        foreach (Match match in MarkedFragmentRegex().Matches(markdown))
        {
            var id = match.Groups["id"].Value;
            var name = match.Groups["name"].Value;
            var text = match.Groups["text"].Value.Trim();
            var startLine = markdown[..match.Index].Count(static character => character == '\n') + 1;
            var endLine = startLine + match.Value.Count(static character => character == '\n');
            fragments.Add(new Fragment(id, noteId, name, "marked", text, startLine, endLine));
        }

        return fragments.OrderBy(static fragment => fragment.StartLine).ToArray();
    }

    [GeneratedRegex("<!--\\s*fragment:\\s*(?<id>[a-zA-Z0-9_\\-]+)\\s+name=\\\"(?<name>[^\\\"]+)\\\"\\s*-->(?<text>.*?)<!--\\s*/fragment\\s*-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkedFragmentRegex();
}
