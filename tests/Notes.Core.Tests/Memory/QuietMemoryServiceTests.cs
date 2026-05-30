using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Xunit;

namespace Notes.Core.Tests.Memory;

public sealed class QuietMemoryServiceTests
{
    [Fact]
    public void SuggestExcludesCurrentNoteAndReturnsRelevantCandidates()
    {
        var now = DateTimeOffset.Now;
        var current = new Note("note_current", "Current", "/current.md", "I am designing quiet memory for Markdown notes", now, now);
        var related = new Note("note_related", "Memory margin", "/related.md", "Quiet memory shows related fragments while writing", now, now);
        var unrelated = new Note("note_unrelated", "Recipe", "/recipe.md", "Pancakes and syrup", now, now);
        var index = new InMemorySearchIndex();
        index.Rebuild(new[] { current, related, unrelated });
        var service = new QuietMemoryService(index);

        var candidates = service.Suggest(new MemoryQuery(current, "quiet memory", 5)).ToArray();

        Assert.Single(candidates);
        Assert.Equal("note_related", candidates[0].Note.Id);
        Assert.Equal("Related note", candidates[0].Kind);
        Assert.Contains("quiet", candidates[0].Reason, StringComparison.OrdinalIgnoreCase);
    }
}
