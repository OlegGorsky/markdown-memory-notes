using Notes.Core.Notes;
using Notes.Core.Search;
using Xunit;

namespace Notes.Core.Tests.Search;

public sealed class InMemorySearchIndexTests
{
    [Fact]
    public void SearchRanksTitleAndBodyMatches()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        index.Rebuild(new[]
        {
            new Note("note_a", "Quiet memory", "/a.md", "Contextual suggestions beside the editor", now, now),
            new Note("note_b", "Trail design", "/b.md", "Routes through ideas", now, now),
            new Note("note_c", "Inbox", "/c.md", "Fast capture", now, now)
        });

        var results = index.Search("quiet suggestions", 5).ToArray();

        Assert.Equal("note_a", results[0].Note.Id);
        Assert.True(results[0].Score > 0);
        Assert.DoesNotContain(results, result => result.Note.Id == "note_c");
    }
}
