using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Core.Trails;

public sealed class TrailRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IFileSystem fileSystem;
    private readonly Func<DateTimeOffset> now;

    public TrailRepository(IFileSystem fileSystem, Func<DateTimeOffset>? now = null)
    {
        this.fileSystem = fileSystem;
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<Trail>> ListAsync(Vault.Vault vault)
    {
        var store = await ReadStoreAsync(vault);
        return store.Trails;
    }

    public async Task<Trail> CreateAsync(Vault.Vault vault, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Trail title cannot be empty.", nameof(title));
        }

        var store = await ReadStoreAsync(vault);
        var timestamp = now();
        var trail = new Trail("trail_" + Guid.NewGuid().ToString("N"), title.Trim(), timestamp, timestamp, Array.Empty<TrailItem>());
        store.Trails.Add(trail);
        await WriteStoreAsync(vault, store);
        return trail;
    }

    public async Task<Trail> AddItemAsync(Vault.Vault vault, string trailId, TrailItem item)
    {
        var store = await ReadStoreAsync(vault);
        var index = store.Trails.FindIndex(trail => trail.Id == trailId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Trail was not found: {trailId}");
        }

        var existing = store.Trails[index];
        var items = existing.Items.Concat(new[] { item }).ToArray();
        var updated = existing with { Items = items, Updated = now() };
        store.Trails[index] = updated;
        await WriteStoreAsync(vault, store);
        return updated;
    }

    private async Task<TrailStore> ReadStoreAsync(Vault.Vault vault)
    {
        if (!await fileSystem.FileExistsAsync(vault.TrailsPath))
        {
            return new TrailStore();
        }

        var text = await fileSystem.ReadAllTextAsync(vault.TrailsPath);
        return JsonSerializer.Deserialize<TrailStore>(text, JsonOptions) ?? new TrailStore();
    }

    private async Task WriteStoreAsync(Vault.Vault vault, TrailStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        await fileSystem.WriteAllTextAsync(vault.TrailsPath, json + Environment.NewLine);
    }

    private sealed class TrailStore
    {
        public List<Trail> Trails { get; set; } = new();
    }
}
