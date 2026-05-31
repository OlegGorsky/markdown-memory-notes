using Notes.Core.Files;

namespace Notes.Core.Inbox;

public sealed class InboxService
{
    private readonly IFileSystem fileSystem;
    private readonly Func<DateTimeOffset> now;

    public InboxService(IFileSystem fileSystem, Func<DateTimeOffset>? now = null)
    {
        this.fileSystem = fileSystem;
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<string> CaptureAsync(Vault.Vault vault, string text)
    {
        return await CaptureAsync(vault, text, now());
    }

    public async Task<string> CaptureAsync(Vault.Vault vault, string text, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Inbox text cannot be empty.", nameof(text));
        }

        var path = GetCapturePath(vault, timestamp);
        var exists = await fileSystem.FileExistsAsync(path);
        var prefix = exists ? (await fileSystem.ReadAllTextAsync(path)).TrimEnd() : $"# Inbox {timestamp:yyyy-MM-dd}";
        var line = $"- {timestamp:HH:mm} {text.Trim()}";
        var next = prefix + Environment.NewLine + line + Environment.NewLine;
        await fileSystem.WriteAllTextAsync(path, next);
        return path;
    }

    public string GetCapturePath(Vault.Vault vault, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return Path.Combine(vault.InboxPath, timestamp.ToString("yyyy-MM-dd") + ".md");
    }
}
