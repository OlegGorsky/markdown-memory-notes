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

    [Fact]
    public void UpsertReplacesExistingNoteWithoutRebuildingWholeIndex()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        var note = new Note("note_a", "Old topic", "/a.md", "alpha draft", now, now);
        index.Rebuild([note]);

        index.Upsert(note with { Title = "New topic", Body = "bravo final", Updated = now.AddMinutes(1) });

        Assert.Empty(index.Search("alpha", 5));
        var results = index.Search("bravo", 5);
        var result = Assert.Single(results);
        Assert.Equal("note_a", result.Note.Id);
        Assert.Equal("New topic", result.Note.Title);
    }

    [Fact]
    public void RemoveDeletesNoteFromIndexWithoutRebuildingWholeIndex()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        index.Rebuild([
            new Note("note_a", "Quiet memory", "/a.md", "Contextual suggestions", now, now),
            new Note("note_b", "Trail design", "/b.md", "Routes through ideas", now, now)
        ]);

        index.Remove("note_a");

        Assert.Empty(index.Search("quiet", 5));
        Assert.Single(index.Search("trail", 5));
    }

    [Fact]
    public void SearchUsesBoundedQueryTerms()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        var terms = Enumerable.Range(0, 40)
            .Select(value => $"term{value:D2}")
            .ToArray();
        index.Rebuild([
            new Note("note_a", "Body only", "/a.md", string.Join(' ', terms), now, now)
        ]);

        var result = Assert.Single(index.Search(string.Join(' ', terms), 5));

        Assert.Equal(64, result.Score);
        Assert.Equal(32, result.MatchedTerms.Count);
    }

    [Fact]
    public void SearchDeduplicatesQueryTerms()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        index.Rebuild([
            new Note("note_a", "Quiet memory", "/a.md", "Quiet suggestions", now, now)
        ]);

        var result = Assert.Single(index.Search("quiet quiet quiet", 5));

        Assert.Equal(7, result.Score);
        Assert.Equal(["quiet"], result.MatchedTerms);
    }

    [Fact]
    public void SearchDoesNotMatchQueryTermsInsideLargerWords()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        index.Rebuild([
            new Note("note_a", "Discarded draft", "/a.md", "The card was never mentioned.", now, now),
            new Note("note_b", "Archive", "/b.md", "A discarded fragment", now, now)
        ]);

        var results = index.Search("card", 5);

        var result = Assert.Single(results);
        Assert.Equal("note_a", result.Note.Id);
    }
}
