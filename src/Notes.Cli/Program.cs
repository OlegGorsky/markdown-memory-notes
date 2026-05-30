using Notes.Core.Files;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;
using CoreVault = Notes.Core.Vault.Vault;

return new CliProgram().Run(args);

internal sealed class CliProgram
{
    public int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        var vaultPath = Environment.GetEnvironmentVariable("MMN_VAULT") ?? Directory.GetCurrentDirectory();
        var fileSystem = new PhysicalFileSystem();
        var vault = new VaultService(fileSystem).Open(vaultPath);
        var notes = new NoteRepository(fileSystem);

        try
        {
            switch (args[0])
            {
                case "add":
                    return Add(vault, fileSystem, args);
                case "find":
                    return Find(vault, notes, args);
                case "trail":
                    return Trail(vault, fileSystem, args);
                case "index":
                    return Index(vault, notes, args);
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Add(CoreVault vault, PhysicalFileSystem fileSystem, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: notes add \"text\"");
            return 2;
        }

        var text = string.Join(' ', args.Skip(1));
        var path = new InboxService(fileSystem).Capture(vault, text);
        Console.WriteLine($"Captured: {path}");
        return 0;
    }

    private static int Find(CoreVault vault, NoteRepository notes, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: notes find \"query\"");
            return 2;
        }

        var query = string.Join(' ', args.Skip(1));
        var index = new InMemorySearchIndex();
        index.Rebuild(notes.List(vault));
        foreach (var result in index.Search(query, 10))
        {
            Console.WriteLine($"{result.Score}\t{result.Note.Title}\t{result.Note.Path}");
        }

        return 0;
    }

    private static int Trail(CoreVault vault, PhysicalFileSystem fileSystem, string[] args)
    {
        var trails = new TrailRepository(fileSystem);
        if (args.Length == 2 && args[1] == "list")
        {
            foreach (var trail in trails.List(vault))
            {
                Console.WriteLine($"{trail.Id}\t{trail.Title}\t{trail.Items.Count} items");
            }

            return 0;
        }

        if (args.Length == 3 && args[1] == "show")
        {
            var trail = trails.List(vault).SingleOrDefault(value => value.Id == args[2] || value.Title.Equals(args[2], StringComparison.OrdinalIgnoreCase));
            if (trail is null)
            {
                Console.Error.WriteLine($"Trail not found: {args[2]}");
                return 1;
            }

            Console.WriteLine(trail.Title);
            foreach (var item in trail.Items)
            {
                Console.WriteLine(item.FragmentId is null ? $"- note:{item.NoteId}" : $"- fragment:{item.NoteId}#{item.FragmentId}");
            }

            return 0;
        }

        Console.Error.WriteLine("Usage: notes trail list | notes trail show <trail>");
        return 2;
    }

    private static int Index(CoreVault vault, NoteRepository notes, string[] args)
    {
        if (args.Length == 2 && args[1] == "rebuild")
        {
            var allNotes = notes.List(vault);
            var index = new InMemorySearchIndex();
            index.Rebuild(allNotes);
            var memory = new QuietMemoryService(index);
            Console.WriteLine($"Indexed {allNotes.Count} notes. Quiet memory ready: {memory.GetType().Name}");
            return 0;
        }

        Console.Error.WriteLine("Usage: notes index rebuild");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Markdown Memory Notes CLI

Usage:
  notes add "text"
  notes find "query"
  notes trail list
  notes trail show <trail>
  notes index rebuild

Environment:
  MMN_VAULT=/path/to/vault
""");
    }
}
