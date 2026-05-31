using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Notes.Core.Sync;

public static partial class SyncContentHash
{
    public static string Compute(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool IsValid(string? hash)
    {
        return hash is not null && HashRegex().IsMatch(hash);
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashRegex();
}
