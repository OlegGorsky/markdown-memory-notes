using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Notes.Core.Sync;

public static partial class SyncPairingCode
{
    public const int MinLength = 22;
    public const int MaxLength = 64;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool IsValid(string? code)
    {
        return code is not null && PairingCodeRegex().IsMatch(code);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{22,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PairingCodeRegex();
}
