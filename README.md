# Markdown Memory Notes

Local-first visual Markdown notes with quiet memory, thought trails, fragments, inbox capture, desktop UI, and CLI.

## Development on NixOS

```bash
nix develop
 dotnet restore MarkdownMemoryNotes.sln
 dotnet build MarkdownMemoryNotes.sln
 dotnet test MarkdownMemoryNotes.sln
```

## Run desktop app

```bash
nix develop --command dotnet run --project src/Notes.Desktop/Notes.Desktop.csproj
```

The desktop MVP creates a local demo vault at:

```text
~/MarkdownMemoryNotesVault
```

## Run CLI

Use `MMN_VAULT` to point the CLI at a vault:

```bash
export MMN_VAULT="$HOME/MarkdownMemoryNotesVault"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- add "Idea about quiet memory"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- find "quiet memory"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- trail list
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- index rebuild
```

## Projects

- `Notes.Core`: vault, Markdown, inbox, fragments, trails, search, quiet memory.
- `Notes.Desktop`: Avalonia desktop app.
- `Notes.Cli`: command-line client over the same core.

## MVP boundaries

This MVP does not include cloud sync, collaboration, plugin marketplace, built-in AI providers, mobile apps, full WYSIWYG editing, event sourcing, encryption, or full transclusion rendering.
