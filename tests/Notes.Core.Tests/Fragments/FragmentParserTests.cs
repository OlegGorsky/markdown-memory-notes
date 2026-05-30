using Notes.Core.Fragments;
using Xunit;

namespace Notes.Core.Tests.Fragments;

public sealed class FragmentParserTests
{
    [Fact]
    public void ParseFindsHeadingsAndMarkedFragments()
    {
        var markdown = """
# Main idea

Paragraph.

<!-- fragment: frag_123 name="Quiet memory" -->
The app suggests relevant notes.
<!-- /fragment -->

## Decision
Chosen stack: C# and Avalonia.
""";

        var fragments = FragmentParser.Parse("note_1", markdown).ToArray();

        Assert.Contains(fragments, fragment => fragment.Id == "note_1#main-idea" && fragment.Name == "Main idea" && fragment.Kind == "heading");
        Assert.Contains(fragments, fragment => fragment.Id == "frag_123" && fragment.Name == "Quiet memory" && fragment.Kind == "marked");
        Assert.Contains(fragments, fragment => fragment.Id == "note_1#decision" && fragment.Name == "Decision" && fragment.Kind == "heading");
    }

    [Fact]
    public void MarkSelectionWrapsExactSelectionWithReadableMarkers()
    {
        var markdown = "Before\nImportant idea\nAfter";

        var marked = FragmentMarker.Mark(markdown, "Important idea", "Core insight", "frag_abc");

        Assert.Contains("<!-- fragment: frag_abc name=\"Core insight\" -->", marked, StringComparison.Ordinal);
        Assert.Contains("Important idea", marked, StringComparison.Ordinal);
        Assert.Contains("<!-- /fragment -->", marked, StringComparison.Ordinal);
    }
}
