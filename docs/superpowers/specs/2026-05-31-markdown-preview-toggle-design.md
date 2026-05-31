# Markdown Preview Toggle: design spec

Date: 2026-05-31
Status: written spec, pending review
Stack: Blazor WASM / Markdig / CSS glassmorphism

## Goal

Add a preview toggle to the Blazor WASM editor page so the user can switch between raw Markdown editing and rendered HTML preview with a single button click.

## Current state

The editor page (`Notes.razor`) has a textarea for raw Markdown input. The desktop app (Avalonia) already has a split editor/preview mode using `Markdig.Markdown.ToHtml()`. The Web app is preview-less.

Markdig 0.40.0 is already referenced by `Notes.Core` (and thus transitively available in the Blazor WASM project).

## Design

### Interaction

A toggle button sits in the editor header, right of the title input. It shows:
- **"Превью"** (eye icon via Unicode) when in editor mode — clicking switches to preview
- **"Редактор"** (pencil icon via Unicode) when in preview mode — clicking switches back

Only one mode is visible at a time. The toggle is a `glass-btn glass-btn-sm`.

### Rendering

Preview mode hides the `<textarea>` and renders a `<div class="md-preview">` filled with `(MarkupString)Markdig.Markdown.ToHtml(_editBody)`. The raw HTML from Markdig is trusted (no user XSS vector — it's the user's own content rendered locally).

Autosave continues to work in both modes (the `_editBody` binding is still active).

### Styling

New CSS section: `.md-preview` — a scrollable container matching the editor area background, with Markdown typography:

- Headings h1–h6: use glassmorphism font variables, scaled down slightly from page-level headings
- Paragraphs: `var(--text-secondary)`, comfortable line-height
- Inline code: mono font, glass-bg background, subtle border-radius
- Code blocks (fenced): mono font, glass-bg background, padding, rounded corners
- Blockquotes: left border in `var(--accent-soft)`, muted text
- Links: `var(--accent)` color with underline on hover
- Lists: proper indent, spacing
- Horizontal rules: subtle `rgba(255,255,255,0.06)` line
- Images: max-width 100%, rounded

### Animation

Use existing `.animate-in` class (fadeIn 0.35s) when switching modes.

## Implementation plan

### Files to change

1. **`src/Notes.Web/Pages/Notes.razor`** — add `_previewMode` bool, toggle button, conditional rendering of textarea vs preview div
2. **`src/Notes.Web/wwwroot/css/app.css`** — add `.md-preview` and child element styles

### No new dependencies

Markdig is already available via `Notes.Core` → `Notes.Web` project reference.

## Testing

- Verify toggle switches between editor and preview
- Verify Markdown renders correctly (headings, code, lists, links, quotes)
- Verify autosave fires in preview mode (edit title → wait 800ms → note persists)
- Verify empty body renders as empty preview (no crash)
- Verify preview scrolls independently of the page on long content
