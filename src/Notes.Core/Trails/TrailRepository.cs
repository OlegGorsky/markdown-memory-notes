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

    public IReadOnlyList<Trail> List(Vault.Vault vault)
    {
        return ReadStore(vault).Trails;
    }

    public Trail Create(Vault.Vault vault, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Trail title cannot be empty.", nameof(title));
        }

        var store = ReadStore(vault);
        var timestamp = now();
        var trail = new Trail("trail_" + Guid.NewGuid().ToString("N"), title.Trim(), timestamp, timestamp, Array.Empty<TrailItem>());
        store.Trails.Add(trail);
        WriteStore(vault, store);
        return trail;
    }

    public Trail AddItem(Vault.Vault vault, string trailId, TrailItem item)
    {
        var store = ReadStore(vault);
        var index = store.Trails.FindIndex(trail => trail.Id == trailId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Trail was not found: {trailId}");
        }

        var existing = store.Trails[index];
        var items = existing.Items.Concat(new[] { item }).ToArray();
        var updated = existing with { Items = items, Updated = now() };
        store.Trails[index] = updated;
        WriteStore(vault, store);
        return updated;
    }

    private TrailStore ReadStore(Vault.Vault vault)
    {
        if (!fileSystem.FileExists(vault.TrailsPath))
        {
            return new TrailStore();
        }

        var text = fileSystem.ReadAllText(vault.TrailsPath);
        return JsonSerializer.Deserialize<TrailStore>(text, JsonOptions) ?? new TrailStore();
    }

    private void WriteStore(Vault.Vault vault, TrailStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        fileSystem.WriteAllText(vault.TrailsPath, json + Environment.NewLine);
    }

    private sealed class TrailStore
    {
        public List<Trail> Trails { get; set; } = new();
    }
}
