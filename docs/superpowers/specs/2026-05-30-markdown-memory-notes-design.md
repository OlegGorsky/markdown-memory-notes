# Markdown Memory Notes: design spec

Date: 2026-05-30
Status: approved direction, awaiting written spec review
Stack: C# / .NET 10 / Avalonia / Markdown files / local index

## Product idea

Markdown Memory Notes is a local-first visual note app for people who think in text, but do not want to maintain a complex plugin system or a heavy cloud workspace. The app keeps ordinary Markdown files as the source of truth and adds a small, opinionated thinking layer: inbox capture, quiet contextual memory, thought trails, and reusable fragments.

The product is not an Obsidian clone and not an AI chat app. Its main promise is: the user writes normal notes, while the app helps them remember what they already thought, how one idea led to another, and which exact fragments can be reused in new contexts.

Working phrase: "Notes that remember how you think." In Russian: "Заметки, которые помнят ход мысли."

## Target user

The first user is a solo knowledge worker: developer, researcher, founder, writer, student, consultant, or technically curious power user. They are comfortable with Markdown as a durable format, but still want a visual interface: a note list, editor, search, preview, context panel, and visible routes through ideas.

They may later use CLI commands or AI/MCP integrations, but the first product experience must not require CLI knowledge.

## Product principles

1. Local files are the source of truth.
2. Indexes are rebuildable caches, not the canonical data store.
3. The UI is visual first; CLI is a power-user client.
4. AI is optional and external at first. The core must be useful without a model provider.
5. The app should preserve the user's thinking process, not only the final note text.
6. Markdown should remain readable outside the app.
7. Features should be few, integrated, and opinionated instead of plugin-driven.

## Core-first architecture

The project should be split into an independent core and clients.

```text
Notes.Core
  Vault access
  Markdown parsing
  Note metadata
  Fragment detection
  Trails model
  Search/index abstractions
  Quiet memory ranking

Notes.Desktop
  Avalonia UI
  Library shell
  Markdown editor
  Preview/split mode
  Inbox
  Quiet memory panel
  Trails and fragments views

Notes.Cli
  add/find/open/trail/context commands
  Uses Notes.Core directly
```

This keeps the product small without trapping logic inside Avalonia. The same core can later power MCP, mobile companion apps, or a local HTTP API.

## Storage model

A vault is a normal directory.

```text
vault/
  notes/
    2026-05-30-idea.md
    project-memory-notes.md
  inbox/
    2026-05-30.md
  .notes/
    index.sqlite
    trails.json
    fragments.json
    settings.json
```

Canonical user content:

- Markdown notes under `notes/` and `inbox/`.
- Optional YAML frontmatter inside notes.
- Stable fragment IDs embedded in Markdown comments or attributes.
- Trails saved as small JSON documents referencing notes and fragment IDs.

Derived content:

- SQLite full-text index.
- Cached note metadata.
- Cached fragment map.
- Cached relationship/ranking data.

If `.notes/index.sqlite` is deleted, the app must rebuild it from Markdown files and trail/fragment metadata.

## MVP features

### 1. Vault

The app opens or creates a vault directory. A vault contains Markdown files and a `.notes` metadata folder. The app should never require cloud login or a remote account.

Minimum behavior:

- create vault;
- open existing vault;
- list Markdown notes;
- create, rename, edit, and delete notes with safe confirmations;
- watch file changes where practical;
- rebuild index on demand.

### 2. Visual library

The first screen is a real working app, not a marketing page. It should have:

- left navigation: Inbox, Notes, Trails, Fragments, Search, Settings;
- note list with title, date, short excerpt, and lightweight indicators;
- central editor/preview workspace;
- right contextual panel for quiet memory, backlinks, fragments, or trail context.

The library should feel closer to a calm research desk than a generic SaaS dashboard.

### 3. Markdown editor

The editor should support plain Markdown editing first. Rich WYSIWYG is not required for MVP.

Minimum behavior:

- edit raw Markdown;
- show rendered preview or split view;
- preserve frontmatter;
- autosave with visible saved/unsaved state;
- avoid corrupting external edits;
- keep Markdown readable in other tools.

### 4. Inbox

Inbox is the fastest capture path. It is for thoughts that do not yet have a place.

Minimum behavior:

- create quick note from the app;
- append to today's inbox note;
- convert an inbox item into a normal note;
- link an inbox item into a trail;
- keep capture friction lower than choosing a folder or tag.

### 5. Quiet memory panel

Quiet memory is the signature feature. It is not a chatbot. It is a passive contextual panel that suggests relevant notes and fragments while the user writes or selects text.

MVP implementation can start with lexical search and simple ranking:

- current note title;
- selected text;
- headings;
- repeated terms;
- linked notes;
- recent notes.

Later, semantic search or local embeddings can improve ranking without changing the UX.

Minimum behavior:

- show related notes/fragments in the right panel;
- explain relevance briefly using product language;
- allow opening a suggestion;
- allow linking suggestion to the current note;
- allow dismissing a suggestion for the current session.

### 6. Thought trails

A trail is a saved route through notes and fragments. It captures the path of reasoning, not just a static relationship graph.

Example:

```text
Idea: local Markdown notes
→ Pain: bloated note apps
→ Reference: Memex trails
→ Design: quiet memory panel
→ Decision: C# + Avalonia core-first MVP
```

Minimum behavior:

- create a trail;
- add current note or selected fragment to a trail;
- reorder trail items;
- open a trail as a linear reading path;
- show trails that include the current note.

Trail item references should be stable enough to survive note renames.

### 7. Live fragments

A fragment is an addressable block inside a Markdown note: a paragraph, heading section, quote, checklist, or decision. The MVP does not need full transclusion rendering, but it should establish stable fragment identity.

Minimum behavior:

- detect headings as addressable fragments;
- allow marking selected text as a named fragment;
- store fragment ID in the Markdown file in a readable way;
- show fragment list for the current note;
- allow trail items and quiet memory suggestions to reference fragments.

Full live transclusion can be a post-MVP feature.

### 8. CLI client

CLI is not the first user interface, but it validates the core architecture.

MVP CLI commands:

```bash
notes add "text"
notes find "query"
notes trail list
notes trail show <trail>
notes index rebuild
```

The CLI should use `Notes.Core`, not duplicate logic from the desktop app.

## UI architecture

### App shell

Use a product app shell, not a long single page.

```text
Top bar
  Vault name
  Global search / command entry
  New note
  Capture
  Settings

Left rail
  Inbox
  Notes
  Trails
  Fragments
  Search
  Settings

Main workspace
  Note list or trail list
  Editor / preview

Right panel
  Quiet memory
  Note outline
  Trails
  Fragments
```

### Primary views

1. Inbox: capture and triage.
2. Notes: library and editor.
3. Trails: saved reasoning paths.
4. Fragments: reusable blocks and references.
5. Search: full-text search across notes/fragments.
6. Settings: vault location, indexing, editor preferences.

### State model

The UI must cover:

- no vault selected;
- empty vault;
- loading/indexing;
- note selected;
- note editing;
- unsaved changes;
- external file changed;
- index rebuild failed;
- no quiet memory suggestions;
- trail empty;
- fragment not found after file edit.

## Data contracts

### Note identity

Each note should have a stable ID in frontmatter when created by the app.

```yaml
---
id: note_01jz...
title: Local Markdown Notes
created: 2026-05-30T12:00:00+03:00
updated: 2026-05-30T12:30:00+03:00
---
```

For existing Markdown files without frontmatter, the app can derive temporary identity from path and content hash, then offer to normalize metadata later.

### Fragment identity

A marked fragment should use a stable ID that remains readable outside the app. Candidate syntax:

```md
<!-- fragment: frag_01jz... name="Quiet memory" -->
The app should suggest relevant previous notes without becoming a chatbot.
<!-- /fragment -->
```

Heading fragments can derive IDs from note ID plus heading slug, with collision handling in the index.

### Trail document

Trails can live in `.notes/trails.json` for MVP.

```json
{
  "trails": [
    {
      "id": "trail_01jz...",
      "title": "Designing Markdown Memory Notes",
      "created": "2026-05-30T12:00:00+03:00",
      "updated": "2026-05-30T12:30:00+03:00",
      "items": [
        {
          "kind": "note",
          "noteId": "note_01jz..."
        },
        {
          "kind": "fragment",
          "noteId": "note_01jz...",
          "fragmentId": "frag_01jz..."
        }
      ]
    }
  ]
}
```

## Search and quiet memory ranking

MVP ranking should be deterministic and local.

Inputs:

- active note title;
- active note headings;
- current selection;
- latest edited paragraph;
- explicit links;
- recency;
- trail membership.

Outputs:

- related notes;
- related fragments;
- trails containing the current note;
- suggested actions: open, link, add to trail, ignore.

Implementation should be layered so that semantic embeddings can be added later as another ranking provider.

## Technology choices

### Runtime and language

Use C# on current LTS .NET. As of 2026-05-30, .NET 10 is the relevant LTS target. If local NixOS packaging makes .NET 10 impractical at implementation time, use the latest available stable SDK in the environment and keep the code compatible with the LTS target where possible.

### Desktop UI

Use Avalonia for Linux/macOS/Windows desktop support. Avoid .NET MAUI because Linux desktop is not the right primary target.

### MVVM

Use CommunityToolkit.Mvvm for observable state and commands. Keep view models thin. Domain logic belongs in `Notes.Core`.

### Markdown

Use Markdig for parsing and rendering Markdown. Avoid tying canonical Markdown storage to a rich-text model in MVP.

### Index

Use SQLite FTS for local full-text search. Keep index rebuildable.

### File watching

Use .NET file watching where practical, but treat it as a convenience. The app should also support manual rescan/rebuild.

## NixOS considerations

The local development machine is NixOS. Implementation should avoid instructions that assume apt, brew, or global mutable package installation.

Project setup should prefer:

- `flake.nix` or `shell.nix` for .NET SDK and native dependencies;
- local `dotnet` commands inside the dev shell;
- no global package-manager setup as a required step;
- reproducible build instructions.

## Non-goals for MVP

- Real-time collaboration.
- Cloud sync service.
- Plugin marketplace.
- Built-in AI provider integration.
- Mobile app.
- Full WYSIWYG block editor.
- Complex graph visualization as the main screen.
- Event-sourced architecture.
- End-to-end encryption.
- Full transclusion rendering.

These can be considered after the app proves its core loop.

## Success criteria

The MVP is successful when a user can:

1. Create/open a local vault.
2. Capture an idea quickly into Inbox.
3. Write and preview Markdown notes.
4. Search across notes.
5. See relevant previous notes or fragments while writing.
6. Save a trail that explains how an idea developed.
7. Mark and reuse fragments as references.
8. Use the app without network access or an account.
9. Open the Markdown files in another editor and still understand them.

## Initial visual direction

Domain concepts:

- desk;
- margins;
- index cards;
- research trail;
- quiet peripheral memory;
- archive drawer;
- annotated manuscript;
- local vault.

Color world:

- warm paper;
- graphite ink;
- muted folder beige;
- soft blue pencil marks;
- amber active marker;
- desaturated green for saved state;
- red only for destructive actions.

Signature:

The signature interaction is the quiet memory margin: a right-side contextual strip that behaves like a thinking margin, not a chatbot window. It should feel like relevant notes surfaced at the edge of attention.

Defaults to reject:

- generic SaaS dashboard with metric cards;
- graph-first note app;
- AI chat as the central product;
- plugin marketplace as the solution to missing product decisions;
- dark terminal aesthetic as the default for all users.

## Open implementation questions

These questions should be resolved during implementation planning, not by changing the product direction:

1. Exact editor control for Avalonia MVP.
2. Exact fragment marker syntax after testing parser behavior.
3. Whether trail metadata starts as one JSON file or one file per trail.
4. Whether SQLite FTS is enough for first quiet memory ranking.
5. Packaging approach for NixOS development and cross-platform releases.
