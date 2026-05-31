using System.Text.Json;

namespace Notes.Core.Sync;

internal static class SyncJsonProperties
{
    public static bool TryGetUniqueProperty(JsonElement element, string name, out JsonElement property)
    {
        property = default;
        if (HasDuplicateProperty(element, name))
        {
            return false;
        }

        return element.TryGetProperty(name, out property);
    }

    private static bool HasDuplicateProperty(JsonElement element, string name)
    {
        var count = 0;
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(name) ||
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                if (count > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
