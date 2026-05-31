using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Notes.Core.Sync;

public static partial class SyncMessageId
{
    public const int Length = 32;

    public static string New()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    public static bool IsValid(string? messageId)
    {
        return messageId is not null && MessageIdRegex().IsMatch(messageId);
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageIdRegex();
}
