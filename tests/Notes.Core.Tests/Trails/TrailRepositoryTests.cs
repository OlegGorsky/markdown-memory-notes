using Notes.Core.Files;
using Notes.Core.Trails;
using Xunit;

namespace Notes.Core.Tests.Trails;

public sealed class TrailRepositoryTests
{
    [Fact]
    public void CreateAndAddItemsPersistsTrailJson()
    {
        var vault = CreateVault();
        var repository = new TrailRepository(new PhysicalFileSystem(), () => new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.FromHours(3)));

        var trail = repository.Create(vault, "Designing memory notes");
        repository.AddItem(vault, trail.Id, TrailItem.Note("note_1"));
        repository.AddItem(vault, trail.Id, TrailItem.Fragment("note_2", "frag_1"));

        var loaded = repository.List(vault).Single();
        Assert.Equal("Designing memory notes", loaded.Title);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal("note", loaded.Items[0].Kind);
        Assert.Equal("fragment", loaded.Items[1].Kind);
        Assert.Contains("trail_", File.ReadAllText(vault.TrailsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ListReturnsEmptyWhenTrailFileIsMissing()
    {
        var vault = CreateVault();
        File.Delete(vault.TrailsPath);
        var repository = new TrailRepository(new PhysicalFileSystem());

        var trails = repository.List(vault);

        Assert.Empty(trails);
    }

    private static global::Notes.Core.Vault.Vault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new global::Notes.Core.Vault.VaultService(new PhysicalFileSystem()).Create(root);
    }
}
