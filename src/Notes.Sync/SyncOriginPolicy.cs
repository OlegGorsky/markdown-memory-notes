namespace Notes.Sync;

public static class SyncOriginPolicy
{
    public static bool IsAllowed(string? origin, IReadOnlyList<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        if (allowedOrigins.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        var normalizedOrigin = NormalizeOrigin(origin);
        if (normalizedOrigin is null)
        {
            return false;
        }

        foreach (var allowedOrigin in allowedOrigins)
        {
            var normalizedAllowedOrigin = NormalizeConfiguredOrigin(allowedOrigin);
            if (normalizedAllowedOrigin is not null &&
                string.Equals(normalizedAllowedOrigin, normalizedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string? NormalizeConfiguredOrigin(string origin)
    {
        return NormalizeOrigin(origin);
    }

    private static string? NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
    }
}
