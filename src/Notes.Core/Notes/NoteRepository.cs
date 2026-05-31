using System.Security.Cryptography;
using System.Text;
using Notes.Core.Files;
using Notes.Core.Markdown;
using Notes.Core.Vault;

namespace Notes.Core.Notes;

public sealed class NoteRepository
{
    private const int MaxConcurrentReads = 8;
    private readonly IFileSystem fileSystem;

    public NoteRepository(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public async Task<Note> CreateAsync(Vault.Vault vault, string title, string body)
    {
        var now = DateTimeOffset.Now;
        var id = "note_" + Guid.NewGuid().ToString("N");
        var slug = NoteTitle.ToSlug(title);
        var path = await UniquePathAsync(vault.NotesPath, slug);
        var noteBody = $"# {title}\n\n{body.Trim()}\n";
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["title"] = title,
            ["created"] = now.ToString("O"),
            ["updated"] = now.ToString("O")
        };

        await fileSystem.WriteAllTextAsync(path, MarkdownParser.Write(frontmatter, noteBody));
        return new Note(id, title, path, noteBody, now, now);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(Vault.Vault vault)
    {
        var files = new List<string>();
        if (await fileSystem.DirectoryExistsAsync(vault.NotesPath))
        {
            files.AddRange(await fileSystem.EnumerateFilesAsync(vault.NotesPath, "*.md", SearchOption.AllDirectories));
        }

        if (await fileSystem.DirectoryExistsAsync(vault.InboxPath))
        {
            files.AddRange(await fileSystem.EnumerateFilesAsync(vault.InboxPath, "*.md", SearchOption.AllDirectories));
        }

        var notes = new Note[files.Count];
        await Parallel.ForAsync(
            0,
            files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentReads },
            async (index, _) =>
            {
                notes[index] = await ReadAsync(files[index], vault.RootPath);
            });

        return notes.OrderByDescending(static note => note.Updated).ToArray();
    }

    public async Task<Note> ReadAsync(string path)
    {
        return await ReadAsync(path, vaultRootPath: null);
    }

    private async Task<Note> ReadAsync(string path, string? vaultRootPath)
    {
        var text = await fileSystem.ReadAllTextAsync(path);
        var document = MarkdownParser.Parse(text);
        var fullPath = Path.GetFullPath(path);
        var id = document.GetFrontmatterValue("id", FallbackIdForPath(path, fullPath, vaultRootPath));
        var title = document.GetFrontmatterValue("title", NoteTitle.FromBodyOrFileName(document.Body, path));
        var created = ParseDate(document.GetFrontmatterValue("created"));
        var updated = ParseDate(document.GetFrontmatterValue("updated"));
        return new Note(id, title, fullPath, document.Body, created, updated);
    }

    public async Task<Note> SaveAsync(Note note)
    {
        var updated = DateTimeOffset.Now;
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = note.Id,
            ["title"] = note.Title,
            ["created"] = note.Created.ToString("O"),
            ["updated"] = updated.ToString("O")
        };
        await fileSystem.WriteAllTextAsync(note.Path, MarkdownParser.Write(frontmatter, note.Body));
        return note with { Updated = updated };
    }

    public Task DeleteAsync(Note note)
    {
        return fileSystem.DeleteFileAsync(note.Path);
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private static string FallbackIdForPath(string path, string fullPath, string? vaultRootPath)
    {
        var identityPath = TryGetVaultRelativeContentPath(path, fullPath, vaultRootPath, out var relativePath)
            ? relativePath
            : fullPath.Replace('\\', '/');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identityPath));
        return "path_" + Convert.ToHexStringLower(hash)[..32];
    }

    private static bool TryGetVaultRelativeContentPath(
        string path,
        string fullPath,
        string? vaultRootPath,
        out string relativePath)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (VaultRelativePath.TryNormalizeMarkdownContentPath(normalizedPath, out relativePath))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            relativePath = string.Empty;
            return false;
        }

        var normalizedRoot = Path.GetFullPath(vaultRootPath).Replace('\\', '/').TrimEnd('/') + "/";
        var normalizedFullPath = fullPath.Replace('\\', '/');
        if (!normalizedFullPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            relativePath = string.Empty;
            return false;
        }

        var candidate = normalizedFullPath[normalizedRoot.Length..];
        return VaultRelativePath.TryNormalizeMarkdownContentPath(candidate, out relativePath);
    }

    private async Task<string> UniquePathAsync(string directory, string slug)
    {
        var candidate = Path.Combine(directory, slug + ".md");
        var index = 2;
        while (await fileSystem.FileExistsAsync(candidate))
        {
            candidate = Path.Combine(directory, $"{slug}-{index}.md");
            index++;
        }

        return candidate;
    }
}
