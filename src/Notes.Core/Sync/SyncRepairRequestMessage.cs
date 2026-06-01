using System.Text;
using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Core.Sync;

public sealed record SyncManifestEntry(string Path, string Hash);

public sealed record SyncRepairManifest(IReadOnlyList<SyncManifestEntry> Entries, bool Truncated);

public sealed record SyncRepairRequest(IReadOnlyList<SyncManifestEntry> Entries, bool Truncated);

public static class SyncRepairRequestMessage
{
    public const int MaxEntries = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(IEnumerable<SyncManifestEntry> entries, bool truncated, string messageId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!SyncMessageId.IsValid(messageId))
        {
            throw new ArgumentException("Sync message id is not valid.", nameof(messageId));
        }

        var normalized = NormalizeEntries(entries);
        return JsonSerializer.Serialize(
            new RepairRequestMessage("repairRequest", normalized, truncated, messageId),
            JsonOptions);
    }

    public static bool IsValid(string json, int maxMessageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        return Encoding.UTF8.GetByteCount(json) <= maxMessageBytes &&
               TryParse(json, out _);
    }

    public static bool TryParse(string json, out SyncRepairRequest request)
    {
        request = new SyncRepairRequest([], Truncated: false);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "type", out var typeElement) ||
                typeElement.ValueKind is not JsonValueKind.String ||
                typeElement.GetString() != "repairRequest" ||
                HasUnsupportedTopLevelPayloadProperty(root) ||
                !HasValidOptionalMessageId(root) ||
                !TryReadTruncated(root, out var truncated) ||
                !SyncJsonProperties.TryGetUniqueProperty(root, "entries", out var entriesElement) ||
                entriesElement.ValueKind is not JsonValueKind.Array ||
                !TryReadEntries(entriesElement, out var entries))
            {
                return false;
            }

            request = new SyncRepairRequest(entries, truncated);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<SyncManifestEntry> NormalizeEntries(IEnumerable<SyncManifestEntry> entries)
    {
        var normalized = new List<SyncManifestEntry>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (normalized.Count >= MaxEntries)
            {
                throw new ArgumentException("Sync repair manifest has too many entries.", nameof(entries));
            }

            if (!TryNormalizeEntry(entry.Path, entry.Hash, out var normalizedEntry) ||
                !paths.Add(normalizedEntry.Path))
            {
                throw new ArgumentException("Sync repair manifest entry is not valid.", nameof(entries));
            }

            normalized.Add(normalizedEntry);
        }

        return normalized;
    }

    private static bool TryReadEntries(JsonElement entriesElement, out IReadOnlyList<SyncManifestEntry> entries)
    {
        var parsed = new List<SyncManifestEntry>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entryElement in entriesElement.EnumerateArray())
        {
            if (parsed.Count >= MaxEntries ||
                entryElement.ValueKind is not JsonValueKind.Object ||
                !SyncJsonProperties.TryGetUniqueProperty(entryElement, "path", out var pathElement) ||
                pathElement.ValueKind is not JsonValueKind.String ||
                !SyncJsonProperties.TryGetUniqueProperty(entryElement, "hash", out var hashElement) ||
                hashElement.ValueKind is not JsonValueKind.String ||
                !TryNormalizeEntry(pathElement.GetString(), hashElement.GetString(), out var entry) ||
                !paths.Add(entry.Path))
            {
                entries = [];
                return false;
            }

            parsed.Add(entry);
        }

        entries = parsed;
        return true;
    }

    private static bool TryNormalizeEntry(string? path, string? hash, out SyncManifestEntry entry)
    {
        entry = new SyncManifestEntry(string.Empty, string.Empty);
        if (!VaultRelativePath.TryNormalizeMarkdownContentPath(path ?? string.Empty, out var normalizedPath) ||
            !SyncContentHash.IsValid(hash))
        {
            return false;
        }

        entry = new SyncManifestEntry(normalizedPath, hash!);
        return true;
    }

    private static bool TryReadTruncated(JsonElement root, out bool truncated)
    {
        truncated = false;
        if (!SyncJsonProperties.TryGetUniqueProperty(root, "truncated", out var truncatedElement))
        {
            return true;
        }

        if (truncatedElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        truncated = truncatedElement.GetBoolean();
        return true;
    }

    private static bool HasValidOptionalMessageId(JsonElement root)
    {
        if (!SyncJsonProperties.TryGetUniqueProperty(root, "messageId", out var messageIdElement))
        {
            return true;
        }

        return messageIdElement.ValueKind is JsonValueKind.String &&
               SyncMessageId.IsValid(messageIdElement.GetString());
    }

    private static bool HasUnsupportedTopLevelPayloadProperty(JsonElement root)
    {
        return HasProperty(root, "path") ||
               HasProperty(root, "content") ||
               HasProperty(root, "baseHash");
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(name) ||
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

#pragma warning disable CA1812 // Instantiated via JSON serialization
    private sealed record RepairRequestMessage(
        string Type,
        IReadOnlyList<SyncManifestEntry> Entries,
        bool Truncated,
        string MessageId);
#pragma warning restore CA1812
}
