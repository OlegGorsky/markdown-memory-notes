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

    public string Capture(Vault.Vault vault, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Inbox text cannot be empty.", nameof(text));
        }

        var timestamp = now();
        var date = timestamp.ToString("yyyy-MM-dd");
        var path = Path.Combine(vault.InboxPath, date + ".md");
        var prefix = fileSystem.FileExists(path) ? fileSystem.ReadAllText(path).TrimEnd() : $"# Inbox {date}";
        var line = $"- {timestamp:HH:mm} {text.Trim()}";
        var next = prefix + Environment.NewLine + line + Environment.NewLine;
        fileSystem.WriteAllText(path, next);
        return path;
    }
}
