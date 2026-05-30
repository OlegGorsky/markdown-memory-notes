using Notes.Core.Files;
using Notes.Core.Trails;
using Xunit;

namespace Notes.Core.Tests.Trails;

public sealed class TrailRepositoryTests
{
    [Fact]
    public async Task CreateAndAddItemsPersistsTrailJson()
    {
        var vault = await CreateVaultAsync();
        var repository = new TrailRepository(new PhysicalFileSystem(), () => new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.FromHours(3)));

        var trail = await repository.CreateAsync(vault, "Designing memory notes");
        await repository.AddItemAsync(vault, trail.Id, TrailItem.Note("note_1"));
        await repository.AddItemAsync(vault, trail.Id, TrailItem.Fragment("note_2", "frag_1"));

        var loaded = (await repository.ListAsync(vault)).Single();
        Assert.Equal("Designing memory notes", loaded.Title);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal("note", loaded.Items[0].Kind);
        Assert.Equal("fragment", loaded.Items[1].Kind);
        Assert.Contains("trail_", File.ReadAllText(vault.TrailsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListReturnsEmptyWhenTrailFileIsMissing()
    {
        var vault = await CreateVaultAsync();
        File.Delete(vault.TrailsPath);
        var repository = new TrailRepository(new PhysicalFileSystem());

        var trails = await repository.ListAsync(vault);

        Assert.Empty(trails);
    }

    private static Task<global::Notes.Core.Vault.Vault> CreateVaultAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new global::Notes.Core.Vault.VaultService(new PhysicalFileSystem()).CreateAsync(root);
    }
}
