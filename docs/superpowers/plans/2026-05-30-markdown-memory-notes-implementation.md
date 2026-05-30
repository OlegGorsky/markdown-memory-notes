# Markdown Memory Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Core-first C# / Avalonia MVP for a local Markdown note app with vault storage, visual desktop UI, inbox capture, quiet memory suggestions, thought trails, fragments, search, and a small CLI.

**Architecture:** The implementation is split into `Notes.Core`, `Notes.Desktop`, `Notes.Cli`, and tests. Markdown files are the source of truth; `.notes` metadata and SQLite/search files are rebuildable derived state. Avalonia owns only presentation and view-model orchestration; all domain behavior lives in `Notes.Core` and is reused by the CLI.

**Tech Stack:** .NET 10 on NixOS via `flake.nix`, C# 14 where available, Avalonia UI, CommunityToolkit.Mvvm, Markdig, xUnit, System.Text.Json, deterministic local search with an in-memory lexical index for MVP plus a future-ready SQLite boundary.

---

## Scope and sequencing

This plan intentionally implements a useful MVP without cloud sync, real-time collaboration, plugin support, built-in AI providers, full WYSIWYG editing, full transclusion rendering, event sourcing, or encryption. The first working product should prove the core loop:

1. Create/open a local vault.
2. Capture text into Inbox.
3. Create and edit Markdown notes visually.
4. Search notes.
5. See quiet memory suggestions.
6. Create trails through notes/fragments.
7. Mark fragments and reference them.
8. Use basic CLI commands against the same core.

## File structure

Create this repository layout:

```text
flake.nix
.gitignore
Directory.Build.props
MarkdownMemoryNotes.sln
src/
  Notes.Core/
    Notes.Core.csproj
    Clock/IClock.cs
    Clock/SystemClock.cs
    Files/IFileSystem.cs
    Files/PhysicalFileSystem.cs
    Fragments/Fragment.cs
    Fragments/FragmentMarker.cs
    Fragments/FragmentParser.cs
    Fragments/FragmentService.cs
    Inbox/InboxService.cs
    Markdown/MarkdownDocument.cs
    Markdown/MarkdownParser.cs
    Memory/MemoryCandidate.cs
    Memory/MemoryQuery.cs
    Memory/QuietMemoryService.cs
    Notes/Note.cs
    Notes/NoteMetadata.cs
    Notes/NoteRepository.cs
    Notes/NoteTitle.cs
    Search/ISearchIndex.cs
    Search/SearchResult.cs
    Search/InMemorySearchIndex.cs
    Trails/Trail.cs
    Trails/TrailItem.cs
    Trails/TrailRepository.cs
    Vault/Vault.cs
    Vault/VaultService.cs
  Notes.Cli/
    Notes.Cli.csproj
    Program.cs
  Notes.Desktop/
    Notes.Desktop.csproj
    App.axaml
    App.axaml.cs
    Program.cs
    Models/NavigationItem.cs
    Services/DesktopVaultSession.cs
    ViewModels/MainWindowViewModel.cs
    ViewModels/NoteEditorViewModel.cs
    ViewModels/NoteListItemViewModel.cs
    ViewModels/QuietMemoryItemViewModel.cs
    ViewModels/TrailViewModel.cs
    Views/MainWindow.axaml
    Views/MainWindow.axaml.cs
tests/
  Notes.Core.Tests/
    Notes.Core.Tests.csproj
    Fragments/FragmentParserTests.cs
    Inbox/InboxServiceTests.cs
    Memory/QuietMemoryServiceTests.cs
    Notes/NoteRepositoryTests.cs
    Search/InMemorySearchIndexTests.cs
    Trails/TrailRepositoryTests.cs
    Vault/VaultServiceTests.cs
README.md
```

Responsibilities:

- `Notes.Core`: all domain logic, vault filesystem layout, Markdown metadata parsing, fragments, trails, inbox, search, and quiet memory ranking.
- `Notes.Desktop`: Avalonia shell and MVVM view models only. It should call `Notes.Core` services rather than reimplement note behavior.
- `Notes.Cli`: thin command parser and output formatter using `Notes.Core`.
- `tests/Notes.Core.Tests`: TDD coverage for all domain behavior before desktop wiring.
- `flake.nix`: reproducible NixOS development shell with .NET SDK 10.

## Task 1: Reproducible .NET solution skeleton

**Files:**
- Create: `flake.nix`
- Create: `Directory.Build.props`
- Create: `MarkdownMemoryNotes.sln`
- Create: `src/Notes.Core/Notes.Core.csproj`
- Create: `src/Notes.Cli/Notes.Cli.csproj`
- Create: `src/Notes.Desktop/Notes.Desktop.csproj`
- Create: `tests/Notes.Core.Tests/Notes.Core.Tests.csproj`
- Modify: `.gitignore`
- Create: `README.md`

- [ ] **Step 1: Write Nix development shell**

Create `flake.nix`:

```nix
{
  description = "Markdown Memory Notes development shell";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
    in
    {
      devShells = forAllSystems (system:
        let
          pkgs = import nixpkgs { inherit system; };
          dotnetSdk = pkgs.dotnetCorePackages.sdk_10_0;
        in
        {
          default = pkgs.mkShell {
            packages = [
              dotnetSdk
              pkgs.git
              pkgs.sqlite
            ];

            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";

            shellHook = ''
              export DOTNET_ROOT=${dotnetSdk}
              export PATH="$DOTNET_ROOT/bin:$PATH"
              echo "Markdown Memory Notes dev shell"
              dotnet --version
            '';
          };
        });
    };
}
```

- [ ] **Step 2: Update `.gitignore`**

Ensure `.gitignore` contains exactly these project-specific rules while preserving the existing `.superpowers/` rule:

```gitignore
.superpowers/

bin/
obj/
.vs/
.vscode/
.idea/
TestResults/
coverage/
*.user
*.suo
*.sqlite
*.sqlite-shm
*.sqlite-wal
.DS_Store
```

- [ ] **Step 3: Create shared build props**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Create project directories**

Run:

```bash
mkdir -p src/Notes.Core src/Notes.Cli src/Notes.Desktop tests/Notes.Core.Tests
```

Expected: directories exist.

- [ ] **Step 5: Create `Notes.Core.csproj`**

Create `src/Notes.Core/Notes.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Markdig" Version="0.40.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create `Notes.Cli.csproj`**

Create `src/Notes.Cli/Notes.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Notes.Core/Notes.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Create `Notes.Desktop.csproj`**

Create `src/Notes.Desktop/Notes.Desktop.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.9" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.9" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.9" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <ProjectReference Include="../Notes.Core/Notes.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Create test project**

Create `tests/Notes.Core.Tests/Notes.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.v3.runner.visualstudio" Version="3.2.0" />
    <ProjectReference Include="../../src/Notes.Core/Notes.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 9: Create solution file**

Run:

```bash
nix develop --command dotnet new sln --name MarkdownMemoryNotes
nix develop --command dotnet sln MarkdownMemoryNotes.sln add src/Notes.Core/Notes.Core.csproj src/Notes.Cli/Notes.Cli.csproj src/Notes.Desktop/Notes.Desktop.csproj tests/Notes.Core.Tests/Notes.Core.Tests.csproj
```

Expected: solution contains four projects.

- [ ] **Step 10: Add temporary CLI entry point**

Create `src/Notes.Cli/Program.cs`:

```csharp
Console.WriteLine("Markdown Memory Notes CLI");
```

- [ ] **Step 11: Add temporary Avalonia entry point**

Create `src/Notes.Desktop/Program.cs`:

```csharp
using Avalonia;

namespace Notes.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
```

Create `src/Notes.Desktop/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Notes.Desktop.App"
             RequestedThemeVariant="Light">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

Create `src/Notes.Desktop/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Notes.Desktop.Views;

namespace Notes.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

Create `src/Notes.Desktop/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Notes.Desktop.Views.MainWindow"
        Width="1200"
        Height="760"
        MinWidth="960"
        MinHeight="640"
        Title="Markdown Memory Notes">
  <TextBlock Text="Markdown Memory Notes"
             HorizontalAlignment="Center"
             VerticalAlignment="Center"
             FontSize="24" />
</Window>
```

Create `src/Notes.Desktop/Views/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Notes.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 12: Add README**

Create `README.md`:

```markdown
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
```

- [ ] **Step 13: Restore and build**

Run:

```bash
nix develop --command dotnet restore MarkdownMemoryNotes.sln
nix develop --command dotnet build MarkdownMemoryNotes.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 14: Commit skeleton**

Run:

```bash
git add flake.nix .gitignore Directory.Build.props MarkdownMemoryNotes.sln README.md src tests
git commit -m "chore: scaffold Markdown Memory Notes solution"
```

## Task 2: Vault model and filesystem abstraction

**Files:**
- Create: `src/Notes.Core/Clock/IClock.cs`
- Create: `src/Notes.Core/Clock/SystemClock.cs`
- Create: `src/Notes.Core/Files/IFileSystem.cs`
- Create: `src/Notes.Core/Files/PhysicalFileSystem.cs`
- Create: `src/Notes.Core/Vault/Vault.cs`
- Create: `src/Notes.Core/Vault/VaultService.cs`
- Test: `tests/Notes.Core.Tests/Vault/VaultServiceTests.cs`

- [ ] **Step 1: Write failing vault tests**

Create `tests/Notes.Core.Tests/Vault/VaultServiceTests.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Vault;
using Xunit;

namespace Notes.Core.Tests.Vault;

public sealed class VaultServiceTests
{
    [Fact]
    public void CreateVaultCreatesExpectedDirectoriesAndSettings()
    {
        var root = TestPaths.CreateTempDirectory();
        var service = new VaultService(new PhysicalFileSystem());

        var vault = service.Create(root);

        Assert.Equal(Path.GetFullPath(root), vault.RootPath);
        Assert.True(Directory.Exists(Path.Combine(root, "notes")));
        Assert.True(Directory.Exists(Path.Combine(root, "inbox")));
        Assert.True(Directory.Exists(Path.Combine(root, ".notes")));
        Assert.True(File.Exists(Path.Combine(root, ".notes", "settings.json")));
    }

    [Fact]
    public void OpenVaultRejectsMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new VaultService(new PhysicalFileSystem());

        var exception = Assert.Throws<DirectoryNotFoundException>(() => service.Open(root));

        Assert.Contains(root, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenVaultCreatesMetadataFolderForExistingMarkdownFolder()
    {
        var root = TestPaths.CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        File.WriteAllText(Path.Combine(root, "notes", "hello.md"), "# Hello");
        var service = new VaultService(new PhysicalFileSystem());

        var vault = service.Open(root);

        Assert.Equal(Path.GetFullPath(root), vault.RootPath);
        Assert.True(Directory.Exists(Path.Combine(root, ".notes")));
        Assert.True(File.Exists(Path.Combine(root, ".notes", "settings.json")));
    }
}

internal static class TestPaths
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter VaultServiceTests
```

Expected: FAIL because `Notes.Core.Files` and `Notes.Core.Vault` types do not exist.

- [ ] **Step 3: Add filesystem abstraction**

Create `src/Notes.Core/Files/IFileSystem.cs`:

```csharp
namespace Notes.Core.Files;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}
```

Create `src/Notes.Core/Files/PhysicalFileSystem.cs`:

```csharp
namespace Notes.Core.Files;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }
}
```

- [ ] **Step 4: Add clock abstraction**

Create `src/Notes.Core/Clock/IClock.cs`:

```csharp
namespace Notes.Core.Clock;

public interface IClock
{
    DateTimeOffset Now { get; }
}
```

Create `src/Notes.Core/Clock/SystemClock.cs`:

```csharp
namespace Notes.Core.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
```

- [ ] **Step 5: Add vault model and service**

Create `src/Notes.Core/Vault/Vault.cs`:

```csharp
namespace Notes.Core.Vault;

public sealed record Vault(string RootPath)
{
    public string NotesPath => Path.Combine(RootPath, "notes");
    public string InboxPath => Path.Combine(RootPath, "inbox");
    public string MetadataPath => Path.Combine(RootPath, ".notes");
    public string SettingsPath => Path.Combine(MetadataPath, "settings.json");
    public string TrailsPath => Path.Combine(MetadataPath, "trails.json");
}
```

Create `src/Notes.Core/Vault/VaultService.cs`:

```csharp
using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Core.Vault;

public sealed class VaultService
{
    private readonly IFileSystem fileSystem;

    public VaultService(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public Vault Create(string rootPath)
    {
        var vault = new Vault(Path.GetFullPath(rootPath));
        fileSystem.CreateDirectory(vault.RootPath);
        EnsureLayout(vault);
        return vault;
    }

    public Vault Open(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        if (!fileSystem.DirectoryExists(fullPath))
        {
            throw new DirectoryNotFoundException($"Vault directory was not found: {rootPath}");
        }

        var vault = new Vault(fullPath);
        EnsureLayout(vault);
        return vault;
    }

    private void EnsureLayout(Vault vault)
    {
        fileSystem.CreateDirectory(vault.NotesPath);
        fileSystem.CreateDirectory(vault.InboxPath);
        fileSystem.CreateDirectory(vault.MetadataPath);

        if (!fileSystem.FileExists(vault.SettingsPath))
        {
            var json = JsonSerializer.Serialize(new VaultSettings(1), new JsonSerializerOptions { WriteIndented = true });
            fileSystem.WriteAllText(vault.SettingsPath, json + Environment.NewLine);
        }

        if (!fileSystem.FileExists(vault.TrailsPath))
        {
            fileSystem.WriteAllText(vault.TrailsPath, "{\n  \"trails\": []\n}" + Environment.NewLine);
        }
    }

    private sealed record VaultSettings(int Version);
}
```

- [ ] **Step 6: Run vault tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter VaultServiceTests
```

Expected: PASS.

- [ ] **Step 7: Commit vault foundation**

Run:

```bash
git add src/Notes.Core tests/Notes.Core.Tests
git commit -m "feat: add vault foundation"
```

## Task 3: Markdown note repository and metadata

**Files:**
- Create: `src/Notes.Core/Markdown/MarkdownDocument.cs`
- Create: `src/Notes.Core/Markdown/MarkdownParser.cs`
- Create: `src/Notes.Core/Notes/Note.cs`
- Create: `src/Notes.Core/Notes/NoteMetadata.cs`
- Create: `src/Notes.Core/Notes/NoteTitle.cs`
- Create: `src/Notes.Core/Notes/NoteRepository.cs`
- Test: `tests/Notes.Core.Tests/Notes/NoteRepositoryTests.cs`

- [ ] **Step 1: Write failing note repository tests**

Create `tests/Notes.Core.Tests/Notes/NoteRepositoryTests.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Notes;
using Notes.Core.Vault;
using Xunit;

namespace Notes.Core.Tests.Notes;

public sealed class NoteRepositoryTests
{
    [Fact]
    public void CreateNoteWritesMarkdownWithFrontmatter()
    {
        var vault = CreateVault();
        var repository = new NoteRepository(new PhysicalFileSystem());

        var note = repository.Create(vault, "Local Markdown Notes", "First paragraph.");

        Assert.StartsWith("note_", note.Id, StringComparison.Ordinal);
        Assert.Equal("Local Markdown Notes", note.Title);
        Assert.EndsWith("local-markdown-notes.md", note.Path, StringComparison.Ordinal);
        var fileText = File.ReadAllText(note.Path);
        Assert.Contains("id: ", fileText, StringComparison.Ordinal);
        Assert.Contains("title: Local Markdown Notes", fileText, StringComparison.Ordinal);
        Assert.Contains("# Local Markdown Notes", fileText, StringComparison.Ordinal);
        Assert.Contains("First paragraph.", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public void ListReadsMarkdownNotesFromNotesAndInbox()
    {
        var vault = CreateVault();
        File.WriteAllText(Path.Combine(vault.NotesPath, "alpha.md"), "---\nid: note_alpha\ntitle: Alpha\ncreated: 2026-05-30T10:00:00+03:00\nupdated: 2026-05-30T10:00:00+03:00\n---\n# Alpha\nBody");
        File.WriteAllText(Path.Combine(vault.InboxPath, "2026-05-30.md"), "# Inbox\nCaptured");
        var repository = new NoteRepository(new PhysicalFileSystem());

        var notes = repository.List(vault).OrderBy(note => note.Title).ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal("Alpha", notes[0].Title);
        Assert.Equal("Inbox", notes[1].Title);
    }

    [Fact]
    public void SavePreservesExistingIdAndUpdatesBody()
    {
        var vault = CreateVault();
        var repository = new NoteRepository(new PhysicalFileSystem());
        var created = repository.Create(vault, "Draft", "Original");

        var saved = repository.Save(created with { Body = "Changed body" });

        Assert.Equal(created.Id, saved.Id);
        var text = File.ReadAllText(created.Path);
        Assert.Contains("Changed body", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Original", text, StringComparison.Ordinal);
    }

    private static Vault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new VaultService(new PhysicalFileSystem()).Create(root);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter NoteRepositoryTests
```

Expected: FAIL because note repository types do not exist.

- [ ] **Step 3: Add Markdown document parser**

Create `src/Notes.Core/Markdown/MarkdownDocument.cs`:

```csharp
namespace Notes.Core.Markdown;

public sealed record MarkdownDocument(IReadOnlyDictionary<string, string> Frontmatter, string Body)
{
    public string GetFrontmatterValue(string key, string fallback = "")
    {
        return Frontmatter.TryGetValue(key, out var value) ? value : fallback;
    }
}
```

Create `src/Notes.Core/Markdown/MarkdownParser.cs`:

```csharp
using System.Text;

namespace Notes.Core.Markdown;

public static class MarkdownParser
{
    public static MarkdownDocument Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new MarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return new MarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var frontmatterText = normalized[4..end];
        var body = normalized[(end + 5)..];
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontmatterText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            frontmatter[key] = value;
        }

        return new MarkdownDocument(frontmatter, body);
    }

    public static string Write(IReadOnlyDictionary<string, string> frontmatter, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        foreach (var pair in frontmatter)
        {
            builder.Append(pair.Key).Append(": ").AppendLine(pair.Value);
        }

        builder.AppendLine("---");
        builder.Append(body.TrimStart());
        if (!builder.ToString().EndsWith("\n", StringComparison.Ordinal))
        {
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Add note models**

Create `src/Notes.Core/Notes/NoteMetadata.cs`:

```csharp
namespace Notes.Core.Notes;

public sealed record NoteMetadata(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated);
```

Create `src/Notes.Core/Notes/Note.cs`:

```csharp
namespace Notes.Core.Notes;

public sealed record Note(
    string Id,
    string Title,
    string Path,
    string Body,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public string Excerpt
    {
        get
        {
            var line = Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .FirstOrDefault(static value => value.Length > 0 && !value.StartsWith("#", StringComparison.Ordinal));
            return line is null ? string.Empty : line.Length <= 140 ? line : line[..140];
        }
    }
}
```

Create `src/Notes.Core/Notes/NoteTitle.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Notes.Core.Notes;

public static class NoteTitle
{
    public static string FromBodyOrFileName(string body, string path)
    {
        var heading = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.StartsWith("# ", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading[2..].Trim();
        }

        return System.IO.Path.GetFileNameWithoutExtension(path).Replace('-', ' ');
    }

    public static string ToSlug(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousDash = false;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "note" : slug;
    }
}
```

- [ ] **Step 5: Add note repository**

Create `src/Notes.Core/Notes/NoteRepository.cs`:

```csharp
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
```

- [ ] **Step 6: Run note tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter NoteRepositoryTests
```

Expected: PASS.

- [ ] **Step 7: Run full core tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit note repository**

Run:

```bash
git add src/Notes.Core tests/Notes.Core.Tests
git commit -m "feat: add markdown note repository"
```

## Task 4: Inbox capture service

**Files:**
- Create: `src/Notes.Core/Inbox/InboxService.cs`
- Test: `tests/Notes.Core.Tests/Inbox/InboxServiceTests.cs`

- [ ] **Step 1: Write failing inbox tests**

Create `tests/Notes.Core.Tests/Inbox/InboxServiceTests.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Inbox;
using Notes.Core.Vault;
using Xunit;

namespace Notes.Core.Tests.Inbox;

public sealed class InboxServiceTests
{
    [Fact]
    public void CaptureAppendsToTodayInboxNote()
    {
        var vault = CreateVault();
        var service = new InboxService(new PhysicalFileSystem(), () => new DateTimeOffset(2026, 5, 30, 14, 15, 0, TimeSpan.FromHours(3)));

        service.Capture(vault, "Idea about quiet memory");
        service.Capture(vault, "Second thought");

        var path = Path.Combine(vault.InboxPath, "2026-05-30.md");
        var text = File.ReadAllText(path);
        Assert.Contains("# Inbox 2026-05-30", text, StringComparison.Ordinal);
        Assert.Contains("- 14:15 Idea about quiet memory", text, StringComparison.Ordinal);
        Assert.Contains("- 14:15 Second thought", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureRejectsBlankText()
    {
        var vault = CreateVault();
        var service = new InboxService(new PhysicalFileSystem(), () => DateTimeOffset.Now);

        var exception = Assert.Throws<ArgumentException>(() => service.Capture(vault, "   "));

        Assert.Contains("Inbox text cannot be empty", exception.Message, StringComparison.Ordinal);
    }

    private static Vault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new VaultService(new PhysicalFileSystem()).Create(root);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter InboxServiceTests
```

Expected: FAIL because `InboxService` does not exist.

- [ ] **Step 3: Implement inbox service**

Create `src/Notes.Core/Inbox/InboxService.cs`:

```csharp
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
```

- [ ] **Step 4: Run inbox tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter InboxServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit inbox service**

Run:

```bash
git add src/Notes.Core/Inbox tests/Notes.Core.Tests/Inbox
git commit -m "feat: add inbox capture"
```

## Task 5: Search index and quiet memory ranking

**Files:**
- Create: `src/Notes.Core/Search/ISearchIndex.cs`
- Create: `src/Notes.Core/Search/SearchResult.cs`
- Create: `src/Notes.Core/Search/InMemorySearchIndex.cs`
- Create: `src/Notes.Core/Memory/MemoryCandidate.cs`
- Create: `src/Notes.Core/Memory/MemoryQuery.cs`
- Create: `src/Notes.Core/Memory/QuietMemoryService.cs`
- Test: `tests/Notes.Core.Tests/Search/InMemorySearchIndexTests.cs`
- Test: `tests/Notes.Core.Tests/Memory/QuietMemoryServiceTests.cs`

- [ ] **Step 1: Write failing search tests**

Create `tests/Notes.Core.Tests/Search/InMemorySearchIndexTests.cs`:

```csharp
using Notes.Core.Notes;
using Notes.Core.Search;
using Xunit;

namespace Notes.Core.Tests.Search;

public sealed class InMemorySearchIndexTests
{
    [Fact]
    public void SearchRanksTitleAndBodyMatches()
    {
        var index = new InMemorySearchIndex();
        var now = DateTimeOffset.Now;
        index.Rebuild(new[]
        {
            new Note("note_a", "Quiet memory", "/a.md", "Contextual suggestions beside the editor", now, now),
            new Note("note_b", "Trail design", "/b.md", "Routes through ideas", now, now),
            new Note("note_c", "Inbox", "/c.md", "Fast capture", now, now)
        });

        var results = index.Search("quiet suggestions", 5).ToArray();

        Assert.Equal("note_a", results[0].Note.Id);
        Assert.True(results[0].Score > 0);
        Assert.DoesNotContain(results, result => result.Note.Id == "note_c");
    }
}
```

- [ ] **Step 2: Write failing quiet memory tests**

Create `tests/Notes.Core.Tests/Memory/QuietMemoryServiceTests.cs`:

```csharp
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Xunit;

namespace Notes.Core.Tests.Memory;

public sealed class QuietMemoryServiceTests
{
    [Fact]
    public void SuggestExcludesCurrentNoteAndReturnsRelevantCandidates()
    {
        var now = DateTimeOffset.Now;
        var current = new Note("note_current", "Current", "/current.md", "I am designing quiet memory for Markdown notes", now, now);
        var related = new Note("note_related", "Memory margin", "/related.md", "Quiet memory shows related fragments while writing", now, now);
        var unrelated = new Note("note_unrelated", "Recipe", "/recipe.md", "Pancakes and syrup", now, now);
        var index = new InMemorySearchIndex();
        index.Rebuild(new[] { current, related, unrelated });
        var service = new QuietMemoryService(index);

        var candidates = service.Suggest(new MemoryQuery(current, "quiet memory", 5)).ToArray();

        Assert.Single(candidates);
        Assert.Equal("note_related", candidates[0].Note.Id);
        Assert.Equal("Related note", candidates[0].Kind);
        Assert.Contains("quiet", candidates[0].Reason, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter "InMemorySearchIndexTests|QuietMemoryServiceTests"
```

Expected: FAIL because search and memory types do not exist.

- [ ] **Step 4: Add search models and index**

Create `src/Notes.Core/Search/SearchResult.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Core.Search;

public sealed record SearchResult(Note Note, int Score, IReadOnlyList<string> MatchedTerms);
```

Create `src/Notes.Core/Search/ISearchIndex.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Core.Search;

public interface ISearchIndex
{
    void Rebuild(IEnumerable<Note> notes);
    IReadOnlyList<SearchResult> Search(string query, int limit);
}
```

Create `src/Notes.Core/Search/InMemorySearchIndex.cs`:

```csharp
using System.Text.RegularExpressions;
using Notes.Core.Notes;

namespace Notes.Core.Search;

public sealed partial class InMemorySearchIndex : ISearchIndex
{
    private readonly List<Note> notes = new();

    public void Rebuild(IEnumerable<Note> notesToIndex)
    {
        notes.Clear();
        notes.AddRange(notesToIndex);
    }

    public IReadOnlyList<SearchResult> Search(string query, int limit)
    {
        var terms = Tokenize(query).ToArray();
        if (terms.Length == 0 || limit <= 0)
        {
            return Array.Empty<SearchResult>();
        }

        return notes.Select(note => Score(note, terms))
            .Where(static result => result.Score > 0)
            .OrderByDescending(static result => result.Score)
            .ThenByDescending(static result => result.Note.Updated)
            .Take(limit)
            .ToArray();
    }

    private static SearchResult Score(Note note, IReadOnlyList<string> terms)
    {
        var title = note.Title.ToLowerInvariant();
        var body = note.Body.ToLowerInvariant();
        var matched = new List<string>();
        var score = 0;

        foreach (var term in terms)
        {
            var termScore = 0;
            if (title.Contains(term, StringComparison.Ordinal))
            {
                termScore += 5;
            }

            if (body.Contains(term, StringComparison.Ordinal))
            {
                termScore += 2;
            }

            if (termScore > 0)
            {
                matched.Add(term);
                score += termScore;
            }
        }

        return new SearchResult(note, score, matched);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        foreach (Match match in WordRegex().Matches(value.ToLowerInvariant()))
        {
            if (match.Value.Length >= 3)
            {
                yield return match.Value;
            }
        }
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
```

- [ ] **Step 5: Add quiet memory service**

Create `src/Notes.Core/Memory/MemoryQuery.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Core.Memory;

public sealed record MemoryQuery(Note CurrentNote, string ContextText, int Limit);
```

Create `src/Notes.Core/Memory/MemoryCandidate.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Core.Memory;

public sealed record MemoryCandidate(Note Note, string Kind, string Reason, int Score);
```

Create `src/Notes.Core/Memory/QuietMemoryService.cs`:

```csharp
using Notes.Core.Search;

namespace Notes.Core.Memory;

public sealed class QuietMemoryService
{
    private readonly ISearchIndex searchIndex;

    public QuietMemoryService(ISearchIndex searchIndex)
    {
        this.searchIndex = searchIndex;
    }

    public IReadOnlyList<MemoryCandidate> Suggest(MemoryQuery query)
    {
        var context = string.IsNullOrWhiteSpace(query.ContextText)
            ? query.CurrentNote.Title + " " + query.CurrentNote.Body
            : query.ContextText;

        return searchIndex.Search(context, query.Limit + 1)
            .Where(result => result.Note.Id != query.CurrentNote.Id)
            .Take(query.Limit)
            .Select(static result => new MemoryCandidate(
                result.Note,
                "Related note",
                "Matched " + string.Join(", ", result.MatchedTerms),
                result.Score))
            .ToArray();
    }
}
```

- [ ] **Step 6: Run search and memory tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter "InMemorySearchIndexTests|QuietMemoryServiceTests"
```

Expected: PASS.

- [ ] **Step 7: Commit search and quiet memory**

Run:

```bash
git add src/Notes.Core/Search src/Notes.Core/Memory tests/Notes.Core.Tests/Search tests/Notes.Core.Tests/Memory
git commit -m "feat: add quiet memory search"
```

## Task 6: Fragment parsing and marking

**Files:**
- Create: `src/Notes.Core/Fragments/Fragment.cs`
- Create: `src/Notes.Core/Fragments/FragmentMarker.cs`
- Create: `src/Notes.Core/Fragments/FragmentParser.cs`
- Create: `src/Notes.Core/Fragments/FragmentService.cs`
- Test: `tests/Notes.Core.Tests/Fragments/FragmentParserTests.cs`

- [ ] **Step 1: Write failing fragment tests**

Create `tests/Notes.Core.Tests/Fragments/FragmentParserTests.cs`:

```csharp
using Notes.Core.Fragments;
using Xunit;

namespace Notes.Core.Tests.Fragments;

public sealed class FragmentParserTests
{
    [Fact]
    public void ParseFindsHeadingsAndMarkedFragments()
    {
        var markdown = """
# Main idea

Paragraph.

<!-- fragment: frag_123 name="Quiet memory" -->
The app suggests relevant notes.
<!-- /fragment -->

## Decision
Chosen stack: C# and Avalonia.
""";

        var fragments = FragmentParser.Parse("note_1", markdown).ToArray();

        Assert.Contains(fragments, fragment => fragment.Id == "note_1#main-idea" && fragment.Name == "Main idea" && fragment.Kind == "heading");
        Assert.Contains(fragments, fragment => fragment.Id == "frag_123" && fragment.Name == "Quiet memory" && fragment.Kind == "marked");
        Assert.Contains(fragments, fragment => fragment.Id == "note_1#decision" && fragment.Name == "Decision" && fragment.Kind == "heading");
    }

    [Fact]
    public void MarkSelectionWrapsExactSelectionWithReadableMarkers()
    {
        var markdown = "Before\nImportant idea\nAfter";

        var marked = FragmentMarker.Mark(markdown, "Important idea", "Core insight", "frag_abc");

        Assert.Contains("<!-- fragment: frag_abc name=\"Core insight\" -->", marked, StringComparison.Ordinal);
        Assert.Contains("Important idea", marked, StringComparison.Ordinal);
        Assert.Contains("<!-- /fragment -->", marked, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter FragmentParserTests
```

Expected: FAIL because fragment types do not exist.

- [ ] **Step 3: Add fragment model and parser**

Create `src/Notes.Core/Fragments/Fragment.cs`:

```csharp
namespace Notes.Core.Fragments;

public sealed record Fragment(string Id, string NoteId, string Name, string Kind, string Text, int StartLine, int EndLine);
```

Create `src/Notes.Core/Fragments/FragmentParser.cs`:

```csharp
using System.Text.RegularExpressions;
using Notes.Core.Notes;

namespace Notes.Core.Fragments;

public static partial class FragmentParser
{
    public static IReadOnlyList<Fragment> Parse(string noteId, string markdown)
    {
        var fragments = new List<Fragment>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var name = line.TrimStart('#').Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fragments.Add(new Fragment($"{noteId}#{NoteTitle.ToSlug(name)}", noteId, name, "heading", line, index + 1, index + 1));
                }
            }
        }

        foreach (Match match in MarkedFragmentRegex().Matches(markdown))
        {
            var id = match.Groups["id"].Value;
            var name = match.Groups["name"].Value;
            var text = match.Groups["text"].Value.Trim();
            var startLine = markdown[..match.Index].Count(static character => character == '\n') + 1;
            var endLine = startLine + match.Value.Count(static character => character == '\n');
            fragments.Add(new Fragment(id, noteId, name, "marked", text, startLine, endLine));
        }

        return fragments.OrderBy(static fragment => fragment.StartLine).ToArray();
    }

    [GeneratedRegex("<!--\\s*fragment:\\s*(?<id>[a-zA-Z0-9_\\-]+)\\s+name=\\\"(?<name>[^\\\"]+)\\\"\\s*-->(?<text>.*?)<!--\\s*/fragment\\s*-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkedFragmentRegex();
}
```

- [ ] **Step 4: Add fragment marker and service**

Create `src/Notes.Core/Fragments/FragmentMarker.cs`:

```csharp
namespace Notes.Core.Fragments;

public static class FragmentMarker
{
    public static string Mark(string markdown, string selectedText, string name, string fragmentId)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            throw new ArgumentException("Selected text cannot be empty.", nameof(selectedText));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Fragment name cannot be empty.", nameof(name));
        }

        var index = markdown.IndexOf(selectedText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("Selected text was not found in the Markdown document.");
        }

        var replacement = $"<!-- fragment: {fragmentId} name=\"{name.Trim()}\" -->\n{selectedText.Trim()}\n<!-- /fragment -->";
        return markdown[..index] + replacement + markdown[(index + selectedText.Length)..];
    }
}
```

Create `src/Notes.Core/Fragments/FragmentService.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Core.Fragments;

public sealed class FragmentService
{
    public IReadOnlyList<Fragment> GetFragments(Note note)
    {
        return FragmentParser.Parse(note.Id, note.Body);
    }

    public Note MarkFragment(Note note, string selectedText, string name)
    {
        var fragmentId = "frag_" + Guid.NewGuid().ToString("N");
        var markedBody = FragmentMarker.Mark(note.Body, selectedText, name, fragmentId);
        return note with { Body = markedBody };
    }
}
```

- [ ] **Step 5: Run fragment tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter FragmentParserTests
```

Expected: PASS.

- [ ] **Step 6: Commit fragments**

Run:

```bash
git add src/Notes.Core/Fragments tests/Notes.Core.Tests/Fragments
git commit -m "feat: add markdown fragments"
```

## Task 7: Thought trails repository

**Files:**
- Create: `src/Notes.Core/Trails/Trail.cs`
- Create: `src/Notes.Core/Trails/TrailItem.cs`
- Create: `src/Notes.Core/Trails/TrailRepository.cs`
- Test: `tests/Notes.Core.Tests/Trails/TrailRepositoryTests.cs`

- [ ] **Step 1: Write failing trail tests**

Create `tests/Notes.Core.Tests/Trails/TrailRepositoryTests.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Trails;
using Notes.Core.Vault;
using Xunit;

namespace Notes.Core.Tests.Trails;

public sealed class TrailRepositoryTests
{
    [Fact]
    public void CreateAndAddItemsPersistsTrailJson()
    {
        var vault = CreateVault();
        var repository = new TrailRepository(new PhysicalFileSystem(), () => new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.FromHours(3)));

        var trail = repository.Create(vault, "Designing memory notes");
        repository.AddItem(vault, trail.Id, TrailItem.Note("note_1"));
        repository.AddItem(vault, trail.Id, TrailItem.Fragment("note_2", "frag_1"));

        var loaded = repository.List(vault).Single();
        Assert.Equal("Designing memory notes", loaded.Title);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal("note", loaded.Items[0].Kind);
        Assert.Equal("fragment", loaded.Items[1].Kind);
        Assert.Contains("trail_", File.ReadAllText(vault.TrailsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ListReturnsEmptyWhenTrailFileIsMissing()
    {
        var vault = CreateVault();
        File.Delete(vault.TrailsPath);
        var repository = new TrailRepository(new PhysicalFileSystem());

        var trails = repository.List(vault);

        Assert.Empty(trails);
    }

    private static Vault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new VaultService(new PhysicalFileSystem()).Create(root);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter TrailRepositoryTests
```

Expected: FAIL because trail types do not exist.

- [ ] **Step 3: Add trail models**

Create `src/Notes.Core/Trails/TrailItem.cs`:

```csharp
namespace Notes.Core.Trails;

public sealed record TrailItem(string Kind, string NoteId, string? FragmentId)
{
    public static TrailItem Note(string noteId) => new("note", noteId, null);

    public static TrailItem Fragment(string noteId, string fragmentId) => new("fragment", noteId, fragmentId);
}
```

Create `src/Notes.Core/Trails/Trail.cs`:

```csharp
namespace Notes.Core.Trails;

public sealed record Trail(
    string Id,
    string Title,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    IReadOnlyList<TrailItem> Items);
```

- [ ] **Step 4: Add trail repository**

Create `src/Notes.Core/Trails/TrailRepository.cs`:

```csharp
using System.Text.Json;
using Notes.Core.Files;

namespace Notes.Core.Trails;

public sealed class TrailRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IFileSystem fileSystem;
    private readonly Func<DateTimeOffset> now;

    public TrailRepository(IFileSystem fileSystem, Func<DateTimeOffset>? now = null)
    {
        this.fileSystem = fileSystem;
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public IReadOnlyList<Trail> List(Vault.Vault vault)
    {
        return ReadStore(vault).Trails;
    }

    public Trail Create(Vault.Vault vault, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Trail title cannot be empty.", nameof(title));
        }

        var store = ReadStore(vault);
        var timestamp = now();
        var trail = new Trail("trail_" + Guid.NewGuid().ToString("N"), title.Trim(), timestamp, timestamp, Array.Empty<TrailItem>());
        store.Trails.Add(trail);
        WriteStore(vault, store);
        return trail;
    }

    public Trail AddItem(Vault.Vault vault, string trailId, TrailItem item)
    {
        var store = ReadStore(vault);
        var index = store.Trails.FindIndex(trail => trail.Id == trailId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Trail was not found: {trailId}");
        }

        var existing = store.Trails[index];
        var items = existing.Items.Concat(new[] { item }).ToArray();
        var updated = existing with { Items = items, Updated = now() };
        store.Trails[index] = updated;
        WriteStore(vault, store);
        return updated;
    }

    private TrailStore ReadStore(Vault.Vault vault)
    {
        if (!fileSystem.FileExists(vault.TrailsPath))
        {
            return new TrailStore();
        }

        var text = fileSystem.ReadAllText(vault.TrailsPath);
        return JsonSerializer.Deserialize<TrailStore>(text, JsonOptions) ?? new TrailStore();
    }

    private void WriteStore(Vault.Vault vault, TrailStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        fileSystem.WriteAllText(vault.TrailsPath, json + Environment.NewLine);
    }

    private sealed class TrailStore
    {
        public List<Trail> Trails { get; set; } = new();
    }
}
```

- [ ] **Step 5: Run trail tests**

Run:

```bash
nix develop --command dotnet test tests/Notes.Core.Tests/Notes.Core.Tests.csproj --filter TrailRepositoryTests
```

Expected: PASS.

- [ ] **Step 6: Commit trails**

Run:

```bash
git add src/Notes.Core/Trails tests/Notes.Core.Tests/Trails
git commit -m "feat: add thought trails"
```

## Task 8: CLI over Notes.Core

**Files:**
- Modify: `src/Notes.Cli/Program.cs`

- [ ] **Step 1: Replace CLI placeholder with command implementation**

Modify `src/Notes.Cli/Program.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;

var exitCode = Run(args);
return exitCode;

static int Run(string[] args)
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

static int Add(Notes.Core.Vault.Vault vault, PhysicalFileSystem fileSystem, string[] args)
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

static int Find(Notes.Core.Vault.Vault vault, NoteRepository notes, string[] args)
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

static int Trail(Notes.Core.Vault.Vault vault, PhysicalFileSystem fileSystem, string[] args)
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

static int Index(Notes.Core.Vault.Vault vault, NoteRepository notes, string[] args)
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

static void PrintHelp()
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
```

- [ ] **Step 2: Build CLI**

Run:

```bash
nix develop --command dotnet build src/Notes.Cli/Notes.Cli.csproj
```

Expected: PASS.

- [ ] **Step 3: Smoke test CLI against a temp vault**

Run:

```bash
VAULT="$(mktemp -d)"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- help
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- index rebuild
```

Expected: help prints usage. The second command fails if run outside a vault. This is acceptable at this task stage because CLI intentionally opens an existing vault path. Use the desktop or core tests to create vaults.

- [ ] **Step 4: Commit CLI**

Run:

```bash
git add src/Notes.Cli/Program.cs
git commit -m "feat: add notes CLI commands"
```

## Task 9: Avalonia desktop shell and view models

**Files:**
- Create: `src/Notes.Desktop/Models/NavigationItem.cs`
- Create: `src/Notes.Desktop/Services/DesktopVaultSession.cs`
- Create: `src/Notes.Desktop/ViewModels/NoteListItemViewModel.cs`
- Create: `src/Notes.Desktop/ViewModels/QuietMemoryItemViewModel.cs`
- Create: `src/Notes.Desktop/ViewModels/TrailViewModel.cs`
- Create: `src/Notes.Desktop/ViewModels/NoteEditorViewModel.cs`
- Create: `src/Notes.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Notes.Desktop/App.axaml.cs`
- Modify: `src/Notes.Desktop/Views/MainWindow.axaml`
- Modify: `src/Notes.Desktop/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add navigation model**

Create `src/Notes.Desktop/Models/NavigationItem.cs`:

```csharp
namespace Notes.Desktop.Models;

public sealed record NavigationItem(string Key, string Label);
```

- [ ] **Step 2: Add desktop vault session service**

Create `src/Notes.Desktop/Services/DesktopVaultSession.cs`:

```csharp
using Notes.Core.Files;
using Notes.Core.Fragments;
using Notes.Core.Inbox;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Search;
using Notes.Core.Trails;
using Notes.Core.Vault;

namespace Notes.Desktop.Services;

public sealed class DesktopVaultSession
{
    private readonly PhysicalFileSystem fileSystem = new();
    private readonly InMemorySearchIndex searchIndex = new();

    public DesktopVaultSession(string rootPath)
    {
        Vault = new VaultService(fileSystem).Create(rootPath);
        Notes = new NoteRepository(fileSystem);
        Inbox = new InboxService(fileSystem);
        Trails = new TrailRepository(fileSystem);
        Fragments = new FragmentService();
        QuietMemory = new QuietMemoryService(searchIndex);
        RebuildIndex();
    }

    public Vault Vault { get; }
    public NoteRepository Notes { get; }
    public InboxService Inbox { get; }
    public TrailRepository Trails { get; }
    public FragmentService Fragments { get; }
    public QuietMemoryService QuietMemory { get; }

    public IReadOnlyList<Note> RebuildIndex()
    {
        var notes = Notes.List(Vault);
        searchIndex.Rebuild(notes);
        return notes;
    }
}
```

- [ ] **Step 3: Add list and panel view models**

Create `src/Notes.Desktop/ViewModels/NoteListItemViewModel.cs`:

```csharp
using Notes.Core.Notes;

namespace Notes.Desktop.ViewModels;

public sealed class NoteListItemViewModel
{
    public NoteListItemViewModel(Note note)
    {
        Note = note;
    }

    public Note Note { get; }
    public string Title => Note.Title;
    public string Excerpt => Note.Excerpt;
    public string Updated => Note.Updated == DateTimeOffset.MinValue ? "Unknown" : Note.Updated.ToString("yyyy-MM-dd HH:mm");
}
```

Create `src/Notes.Desktop/ViewModels/QuietMemoryItemViewModel.cs`:

```csharp
using Notes.Core.Memory;

namespace Notes.Desktop.ViewModels;

public sealed class QuietMemoryItemViewModel
{
    public QuietMemoryItemViewModel(MemoryCandidate candidate)
    {
        Candidate = candidate;
    }

    public MemoryCandidate Candidate { get; }
    public string Title => Candidate.Note.Title;
    public string Reason => Candidate.Reason;
    public string Score => Candidate.Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
```

Create `src/Notes.Desktop/ViewModels/TrailViewModel.cs`:

```csharp
using Notes.Core.Trails;

namespace Notes.Desktop.ViewModels;

public sealed class TrailViewModel
{
    public TrailViewModel(Trail trail)
    {
        Trail = trail;
    }

    public Trail Trail { get; }
    public string Title => Trail.Title;
    public string Count => $"{Trail.Items.Count} items";
}
```

- [ ] **Step 4: Add note editor view model**

Create `src/Notes.Desktop/ViewModels/NoteEditorViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Markdig;
using Notes.Core.Notes;
using Notes.Desktop.Services;

namespace Notes.Desktop.ViewModels;

public sealed partial class NoteEditorViewModel : ObservableObject
{
    private readonly DesktopVaultSession session;

    [ObservableProperty]
    private Note? note;

    [ObservableProperty]
    private string markdown = string.Empty;

    [ObservableProperty]
    private string previewHtml = string.Empty;

    [ObservableProperty]
    private string saveState = "Saved";

    public NoteEditorViewModel(DesktopVaultSession session)
    {
        this.session = session;
    }

    public void Load(Note selectedNote)
    {
        Note = selectedNote;
        Markdown = selectedNote.Body;
        PreviewHtml = Markdig.Markdown.ToHtml(Markdown);
        SaveState = "Saved";
    }

    partial void OnMarkdownChanged(string value)
    {
        PreviewHtml = Markdig.Markdown.ToHtml(value);
        SaveState = Note is null ? "No note" : "Unsaved";
    }

    public Note? Save()
    {
        if (Note is null)
        {
            return null;
        }

        var saved = session.Notes.Save(Note with { Body = Markdown });
        Note = saved;
        SaveState = "Saved";
        session.RebuildIndex();
        return saved;
    }
}
```

- [ ] **Step 5: Add main window view model**

Create `src/Notes.Desktop/ViewModels/MainWindowViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Core.Memory;
using Notes.Core.Notes;
using Notes.Core.Trails;
using Notes.Desktop.Models;
using Notes.Desktop.Services;

namespace Notes.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly DesktopVaultSession session;

    [ObservableProperty]
    private NoteListItemViewModel? selectedNote;

    [ObservableProperty]
    private string captureText = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string status = "Ready";

    public MainWindowViewModel()
    {
        var demoVault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MarkdownMemoryNotesVault");
        session = new DesktopVaultSession(demoVault);
        Editor = new NoteEditorViewModel(session);
        Navigation = new ObservableCollection<NavigationItem>
        {
            new("inbox", "Inbox"),
            new("notes", "Notes"),
            new("trails", "Trails"),
            new("fragments", "Fragments"),
            new("search", "Search"),
            new("settings", "Settings")
        };
        Notes = new ObservableCollection<NoteListItemViewModel>();
        QuietMemory = new ObservableCollection<QuietMemoryItemViewModel>();
        Trails = new ObservableCollection<TrailViewModel>();
        Reload();
    }

    public string VaultName => session.Vault.RootPath;
    public ObservableCollection<NavigationItem> Navigation { get; }
    public ObservableCollection<NoteListItemViewModel> Notes { get; }
    public ObservableCollection<QuietMemoryItemViewModel> QuietMemory { get; }
    public ObservableCollection<TrailViewModel> Trails { get; }
    public NoteEditorViewModel Editor { get; }

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        Editor.Load(value.Note);
        RefreshQuietMemory(value.Note, value.Note.Title + " " + value.Note.Body);
    }

    [RelayCommand]
    private void NewNote()
    {
        var note = session.Notes.Create(session.Vault, "Untitled note", "Start writing here.");
        Reload();
        SelectedNote = Notes.FirstOrDefault(item => item.Note.Id == note.Id);
        Status = "Note created";
    }

    [RelayCommand]
    private void SaveNote()
    {
        var saved = Editor.Save();
        if (saved is not null)
        {
            Reload();
            SelectedNote = Notes.FirstOrDefault(item => item.Note.Id == saved.Id);
            Status = "Saved";
        }
    }

    [RelayCommand]
    private void Capture()
    {
        if (string.IsNullOrWhiteSpace(CaptureText))
        {
            Status = "Capture is empty";
            return;
        }

        session.Inbox.Capture(session.Vault, CaptureText);
        CaptureText = string.Empty;
        Reload();
        Status = "Captured";
    }

    [RelayCommand]
    private void Search()
    {
        Notes.Clear();
        var allNotes = session.RebuildIndex();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? allNotes
            : allNotes.Where(note => note.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || note.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var note in filtered)
        {
            Notes.Add(new NoteListItemViewModel(note));
        }

        Status = $"{Notes.Count} notes";
    }

    [RelayCommand]
    private void CreateTrail()
    {
        var trail = session.Trails.Create(session.Vault, "New thought trail");
        if (SelectedNote is not null)
        {
            session.Trails.AddItem(session.Vault, trail.Id, TrailItem.Note(SelectedNote.Note.Id));
        }

        ReloadTrails();
        Status = "Trail created";
    }

    private void Reload()
    {
        Notes.Clear();
        foreach (var note in session.RebuildIndex())
        {
            Notes.Add(new NoteListItemViewModel(note));
        }

        ReloadTrails();
        Status = $"{Notes.Count} notes";
    }

    private void ReloadTrails()
    {
        Trails.Clear();
        foreach (var trail in session.Trails.List(session.Vault))
        {
            Trails.Add(new TrailViewModel(trail));
        }
    }

    private void RefreshQuietMemory(Note note, string context)
    {
        QuietMemory.Clear();
        foreach (var candidate in session.QuietMemory.Suggest(new MemoryQuery(note, context, 5)))
        {
            QuietMemory.Add(new QuietMemoryItemViewModel(candidate));
        }
    }
}
```

- [ ] **Step 6: Wire view model in app startup**

Modify `src/Notes.Desktop/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Notes.Desktop.ViewModels;
using Notes.Desktop.Views;

namespace Notes.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 7: Replace main window XAML with product shell**

Modify `src/Notes.Desktop/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Notes.Desktop.ViewModels"
        x:Class="Notes.Desktop.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Width="1280"
        Height="800"
        MinWidth="1040"
        MinHeight="680"
        Title="Markdown Memory Notes"
        Background="#F4EFE6">
  <Grid RowDefinitions="56,*,28" ColumnDefinitions="180,320,*,300">
    <Border Grid.Row="0" Grid.ColumnSpan="4" Background="#EEE6D8" BorderBrush="#D8CBB7" BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*,360,96,96" Margin="16,8">
        <TextBlock Text="{Binding VaultName}" VerticalAlignment="Center" FontWeight="SemiBold" Foreground="#332F29" />
        <TextBox Grid.Column="1" Text="{Binding SearchText}" Watermark="Search notes" />
        <Button Grid.Column="2" Content="Search" Command="{Binding SearchCommand}" Margin="8,0,0,0" />
        <Button Grid.Column="3" Content="New" Command="{Binding NewNoteCommand}" Margin="8,0,0,0" />
      </Grid>
    </Border>

    <Border Grid.Row="1" Grid.Column="0" Background="#F4EFE6" BorderBrush="#D8CBB7" BorderThickness="0,0,1,0" Padding="12">
      <ListBox ItemsSource="{Binding Navigation}" SelectedIndex="1">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <TextBlock Text="{Binding Label}" Margin="4,8" />
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
    </Border>

    <Border Grid.Row="1" Grid.Column="1" Background="#FAF7F0" BorderBrush="#D8CBB7" BorderThickness="0,0,1,0" Padding="12">
      <Grid RowDefinitions="Auto,*">
        <StackPanel Spacing="8">
          <TextBlock Text="Inbox" FontWeight="Bold" />
          <TextBox Text="{Binding CaptureText}" Watermark="Capture a thought" AcceptsReturn="True" MinHeight="64" />
          <Button Content="Capture" Command="{Binding CaptureCommand}" />
          <TextBlock Text="Notes" FontWeight="Bold" Margin="0,12,0,0" />
        </StackPanel>
        <ListBox Grid.Row="1" ItemsSource="{Binding Notes}" SelectedItem="{Binding SelectedNote}" Margin="0,12,0,0">
          <ListBox.ItemTemplate>
            <DataTemplate DataType="vm:NoteListItemViewModel">
              <StackPanel Margin="0,8">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap" />
                <TextBlock Text="{Binding Excerpt}" FontSize="12" Opacity="0.72" TextWrapping="Wrap" MaxHeight="36" />
                <TextBlock Text="{Binding Updated}" FontSize="11" Opacity="0.55" />
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Grid>
    </Border>

    <Grid Grid.Row="1" Grid.Column="2" RowDefinitions="Auto,*,180" Background="#FFFDF8" Margin="0">
      <Border BorderBrush="#D8CBB7" BorderThickness="0,0,0,1" Padding="12">
        <Grid ColumnDefinitions="*,96">
          <TextBlock Text="{Binding Editor.SaveState}" VerticalAlignment="Center" Foreground="#6E6252" />
          <Button Grid.Column="1" Content="Save" Command="{Binding SaveNoteCommand}" />
        </Grid>
      </Border>
      <TextBox Grid.Row="1"
               Text="{Binding Editor.Markdown}"
               AcceptsReturn="True"
               FontFamily="Cascadia Mono, JetBrains Mono, monospace"
               FontSize="14"
               Background="#FFFDF8"
               Foreground="#2E2A25"
               BorderThickness="0"
               Padding="18"
               TextWrapping="Wrap" />
      <Border Grid.Row="2" BorderBrush="#D8CBB7" BorderThickness="0,1,0,0" Padding="12" Background="#FAF7F0">
        <ScrollViewer>
          <TextBlock Text="{Binding Editor.PreviewHtml}" TextWrapping="Wrap" FontFamily="serif" />
        </ScrollViewer>
      </Border>
    </Grid>

    <Border Grid.Row="1" Grid.Column="3" Background="#F7F1E8" BorderBrush="#D8CBB7" BorderThickness="1,0,0,0" Padding="14">
      <Grid RowDefinitions="Auto,*,Auto,*">
        <TextBlock Text="Quiet memory" FontWeight="Bold" Foreground="#332F29" />
        <ListBox Grid.Row="1" ItemsSource="{Binding QuietMemory}" Margin="0,10,0,16">
          <ListBox.ItemTemplate>
            <DataTemplate DataType="vm:QuietMemoryItemViewModel">
              <StackPanel Margin="0,8">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap" />
                <TextBlock Text="{Binding Reason}" FontSize="12" Opacity="0.72" TextWrapping="Wrap" />
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto">
          <TextBlock Text="Trails" FontWeight="Bold" />
          <Button Grid.Column="1" Content="New" Command="{Binding CreateTrailCommand}" />
        </Grid>
        <ListBox Grid.Row="3" ItemsSource="{Binding Trails}" Margin="0,10,0,0">
          <ListBox.ItemTemplate>
            <DataTemplate DataType="vm:TrailViewModel">
              <StackPanel Margin="0,8">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap" />
                <TextBlock Text="{Binding Count}" FontSize="12" Opacity="0.7" />
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Grid>
    </Border>

    <Border Grid.Row="2" Grid.ColumnSpan="4" Background="#EEE6D8" BorderBrush="#D8CBB7" BorderThickness="0,1,0,0" Padding="12,4">
      <TextBlock Text="{Binding Status}" Foreground="#6E6252" FontSize="12" />
    </Border>
  </Grid>
</Window>
```

- [ ] **Step 8: Keep code-behind minimal**

Ensure `src/Notes.Desktop/Views/MainWindow.axaml.cs` is:

```csharp
using Avalonia.Controls;

namespace Notes.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 9: Build desktop app**

Run:

```bash
nix develop --command dotnet build src/Notes.Desktop/Notes.Desktop.csproj
```

Expected: PASS.

- [ ] **Step 10: Commit desktop shell**

Run:

```bash
git add src/Notes.Desktop
git commit -m "feat: add Avalonia desktop shell"
```

## Task 10: End-to-end verification and README update

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README with run commands**

Modify `README.md`:

```markdown
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
```

- [ ] **Step 2: Run full restore/build/test**

Run:

```bash
nix develop --command dotnet restore MarkdownMemoryNotes.sln
nix develop --command dotnet build MarkdownMemoryNotes.sln --no-restore
nix develop --command dotnet test MarkdownMemoryNotes.sln --no-build
```

Expected: all commands PASS.

- [ ] **Step 3: Run CLI smoke test with a vault created by the desktop default path or a core-created temp vault**

Run:

```bash
VAULT="$HOME/MarkdownMemoryNotesVault"
mkdir -p "$VAULT"
MMN_VAULT="$VAULT" nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- add "CLI smoke test quiet memory"
MMN_VAULT="$VAULT" nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- find "quiet memory"
```

Expected: `add` prints `Captured:` and `find` prints at least one result after the inbox file is indexed by repository listing.

- [ ] **Step 4: Check git status**

Run:

```bash
git status --short
```

Expected: only intentional README changes are unstaged before commit.

- [ ] **Step 5: Commit verification docs**

Run:

```bash
git add README.md
git commit -m "docs: add MVP run instructions"
```

- [ ] **Step 6: Final verification status**

Run:

```bash
git status --short
```

Expected: clean working tree.

## Self-review

Spec coverage:

- Vault creation/opening/listing: Tasks 2 and 3.
- Markdown editor and visual library: Task 9.
- Inbox capture: Task 4 and Task 9.
- Quiet memory: Task 5 and Task 9.
- Thought trails: Task 7 and Task 9.
- Fragments: Task 6.
- CLI: Task 8.
- NixOS reproducible setup: Task 1.
- Tests and verification: Tasks 2 through 7 and Task 10.

Known MVP simplifications accepted by the spec:

- Search starts with deterministic in-memory lexical search instead of SQLite FTS. The boundary is `ISearchIndex`, so SQLite FTS can replace it without UI changes.
- Desktop preview displays rendered HTML text in a plain text block for MVP. A richer preview control can replace it later.
- Desktop vault selection starts with a deterministic default vault path. A folder picker can be added after the core loop works.
- Fragment UI is not fully exposed in Task 9, but the core fragment service and trail references are in place for the next UI iteration.

Placeholder scan:

- The plan avoids unresolved placeholders and includes concrete file paths, code content, commands, and expected results.
- Every task includes exact file paths, code content, commands, and expected results.

Type consistency:

- `Vault`, `Note`, `Trail`, `TrailItem`, `Fragment`, `SearchResult`, and `MemoryCandidate` names are introduced before use.
- Desktop view models call public methods defined in core tasks.
- CLI calls the same core services as desktop.
