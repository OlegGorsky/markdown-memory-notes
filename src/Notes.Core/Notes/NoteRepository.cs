using Notes.Core.Files;
using Notes.Core.Markdown;
using Notes.Core.Vault;

namespace Notes.Core.Notes;

public sealed class NoteRepository
{
    private readonly IFileSystem fileSystem;

    public NoteRepository(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public Note Create(Vault.Vault vault, string title, string body)
    {
        var now = DateTimeOffset.Now;
        var id = "note_" + Guid.NewGuid().ToString("N");
        var slug = NoteTitle.ToSlug(title);
        var path = UniquePath(vault.NotesPath, slug);
        var noteBody = $"# {title}\n\n{body.Trim()}\n";
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["title"] = title,
            ["created"] = now.ToString("O"),
            ["updated"] = now.ToString("O")
        };

        fileSystem.WriteAllText(path, MarkdownParser.Write(frontmatter, noteBody));
        return new Note(id, title, path, noteBody, now, now);
    }

    public IReadOnlyList<Note> List(Vault.Vault vault)
    {
        var files = new List<string>();
        if (fileSystem.DirectoryExists(vault.NotesPath))
        {
            files.AddRange(fileSystem.EnumerateFiles(vault.NotesPath, "*.md", SearchOption.AllDirectories));
        }

        if (fileSystem.DirectoryExists(vault.InboxPath))
        {
            files.AddRange(fileSystem.EnumerateFiles(vault.InboxPath, "*.md", SearchOption.AllDirectories));
        }

        return files.Select(Read).OrderByDescending(static note => note.Updated).ToArray();
    }

    public Note Read(string path)
    {
        var text = fileSystem.ReadAllText(path);
        var document = MarkdownParser.Parse(text);
        var id = document.GetFrontmatterValue("id", "path_" + Guid.NewGuid().ToString("N"));
        var title = document.GetFrontmatterValue("title", NoteTitle.FromBodyOrFileName(document.Body, path));
        var created = ParseDate(document.GetFrontmatterValue("created"));
        var updated = ParseDate(document.GetFrontmatterValue("updated"));
        return new Note(id, title, Path.GetFullPath(path), document.Body, created, updated);
    }

    public Note Save(Note note)
    {
        var updated = DateTimeOffset.Now;
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = note.Id,
            ["title"] = note.Title,
            ["created"] = note.Created.ToString("O"),
            ["updated"] = updated.ToString("O")
        };
        fileSystem.WriteAllText(note.Path, MarkdownParser.Write(frontmatter, note.Body));
        return note with { Updated = updated };
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private string UniquePath(string directory, string slug)
    {
        var candidate = Path.Combine(directory, slug + ".md");
        var index = 2;
        while (fileSystem.FileExists(candidate))
        {
            candidate = Path.Combine(directory, $"{slug}-{index}.md");
            index++;
        }

        return candidate;
    }
}
