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
- `Notes.Desktop`: Avalonia desktop app (Linux, macOS, Windows).
- `Notes.Cli`: command-line client over the same core.
- `Notes.Mobile`: Avalonia mobile app (Android + iOS).

## Publish & Run

```bash
# Build all desktop platforms:
./publish-all.sh

# Run desktop (auto-detects NixOS):
./run-desktop.sh

# Or run directly:
nix develop --command dotnet run --project src/Notes.Desktop/Notes.Desktop.csproj
```

Desktop outputs in `publish/<rid>/`:

| RID | Platform |
|-----|----------|
| `linux-x64` | Linux x86_64 |
| `win-x64` | Windows x64 |
| `osx-x64` | macOS Intel |
| `osx-arm64` | macOS Apple Silicon |

> On NixOS, use `nix develop` + `dotnet run`. Self-contained binaries work on standard distributions.

### Mobile

Mobile projects require platform workloads not available in the Nix .NET SDK.
Build on a machine with full .NET SDK:

```bash
# Android (needs .NET Android workload + Android SDK):
dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-android -c Release

# iOS (needs macOS + Xcode + .NET iOS workload):
dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-ios -c Release
```
