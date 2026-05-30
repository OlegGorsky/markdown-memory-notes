namespace Notes.Core.Fragments;

public static class FragmentMarker
{
    public static string Mark(string markdown, string selectedText, string name, string fragmentId)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            throw new ArgumentException("Selected text cannot be empty.", nameof(selectedText));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Fragment name cannot be empty.", nameof(name));
        }

        var index = markdown.IndexOf(selectedText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("Selected text was not found in the Markdown document.");
        }

        var replacement = $"<!-- fragment: {fragmentId} name=\"{name.Trim()}\" -->\n{selectedText.Trim()}\n<!-- /fragment -->";
        return markdown[..index] + replacement + markdown[(index + selectedText.Length)..];
    }
}
