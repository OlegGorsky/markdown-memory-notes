# Markdown Memory Notes

Local-first visual Markdown notes with quiet memory, thought trails, fragments, inbox capture, desktop UI, and CLI.

## Development on NixOS

```bash
nix develop
 dotnet restore
 dotnet build MarkdownMemoryNotes.sln
 dotnet test MarkdownMemoryNotes.sln
```

## Projects

- `Notes.Core`: vault, Markdown, inbox, fragments, trails, search, quiet memory.
- `Notes.Desktop`: Avalonia desktop app.
- `Notes.Cli`: command-line client over the same core.
