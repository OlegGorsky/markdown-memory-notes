# Markdown Preview Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a preview-toggle button to the Blazor WASM editor so the user can switch between raw Markdown editing and rendered HTML.

**Architecture:** A `_previewMode` bool in `Notes.razor` controls whether a `<textarea>` or a `<div>` with Markdig-rendered HTML is shown. A toggle button in the editor header switches modes. New CSS styles `.md-preview` in `app.css` provide Markdown typography.

**Tech Stack:** Blazor WASM, Markdig 0.40.0 (already referenced via Notes.Core), CSS glassmorphism

---

### Task 1: Add preview toggle logic and UI to Notes.razor

**Files:**
- Modify: `src/Notes.Web/Pages/Notes.razor`

- [ ] **Step 1: Add `_previewMode` field and toggle method**

In the `@code` block, add after the existing fields:

```csharp
private bool _previewMode;
```

- [ ] **Step 2: Add toggle button in editor-header**

In the template, inside the `@if (_selectedNote is not null)` block's `editor-header` div, add the toggle button after the title input:

```razor
<div class="editor-header">
    <input @bind="_editTitle" @bind:event="oninput" @bind:after="OnFieldChanged"
           class="glass-input" placeholder="Заголовок" />
    <button class="glass-btn glass-btn-sm" @onclick="() => _previewMode = !_previewMode">
        @(_previewMode ? "✎ Ред." : "◉ Превью")
    </button>
</div>
```

- [ ] **Step 3: Conditional render — preview or textarea**

Replace the existing `<textarea>` line:

```razor
<textarea @bind="_editBody" @bind:event="oninput" @bind:after="OnFieldChanged"
          class="editor-textarea" rows="20"></textarea>
```

With conditional rendering:

```razor
@if (_previewMode)
{
    <div class="md-preview animate-in">
        @((MarkupString)Markdig.Markdown.ToHtml(_editBody))
    </div>
}
else
{
    <textarea @bind="_editBody" @bind:event="oninput" @bind:after="OnFieldChanged"
              class="editor-textarea" rows="20"></textarea>
}
```

- [ ] **Step 4: Add Markdig using directive**

In `_Imports.razor`, ensure `Markdig` is available. Check current content — if not present, add:

```razor
@using Markdig
```

`_Imports.razor` already has `@using Notes.Core.Markdown` — but `Markdig.Markdown.ToHtml()` needs the `Markdig` namespace. Add it.

- [ ] **Step 5: Commit**

```bash
git add src/Notes.Web/Pages/Notes.razor src/Notes.Web/_Imports.razor
git commit -m "feat: add markdown preview toggle to editor"
```

---

### Task 2: Add Markdown preview CSS styles

**Files:**
- Modify: `src/Notes.Web/wwwroot/css/app.css`

- [ ] **Step 1: Add `.md-preview` container and typography styles**

Add after the `.editor-textarea` section (before the Badge section):

```css
/* ── Markdown Preview ─────────────────────── */
.md-preview {
    flex: 1;
    padding: 24px;
    overflow-y: auto;
    line-height: 1.8;
    color: var(--text-primary);
    font-family: var(--font);
    font-size: 0.92rem;
}

.md-preview h1 { font-size: 1.7rem; font-weight: 600; margin: 0 0 16px; letter-spacing: -0.02em; }
.md-preview h2 { font-size: 1.35rem; font-weight: 600; margin: 28px 0 12px; letter-spacing: -0.01em; }
.md-preview h3 { font-size: 1.15rem; font-weight: 600; margin: 22px 0 10px; }
.md-preview h4, .md-preview h5, .md-preview h6 {
    font-size: 1rem; font-weight: 600; margin: 18px 0 8px;
    color: var(--text-secondary); text-transform: none; letter-spacing: 0;
}

.md-preview p { margin: 0 0 12px; color: var(--text-secondary); }
.md-preview a { color: var(--accent); text-decoration: none; }
.md-preview a:hover { text-decoration: underline; }

.md-preview code {
    font-family: var(--font-mono);
    font-size: 0.82rem;
    background: var(--glass-bg-active);
    padding: 2px 6px;
    border-radius: 4px;
    color: var(--text-primary);
}

.md-preview pre {
    background: var(--glass-bg);
    border-radius: var(--radius-sm);
    padding: 16px;
    overflow-x: auto;
    margin: 12px 0;
}

.md-preview pre code {
    background: transparent;
    padding: 0;
    font-size: 0.82rem;
    line-height: 1.65;
}

.md-preview blockquote {
    border-left: 2px solid var(--accent-soft);
    margin: 12px 0;
    padding: 4px 0 4px 16px;
    color: var(--text-muted);
}

.md-preview ul, .md-preview ol {
    padding-left: 24px;
    margin: 8px 0 12px;
    color: var(--text-secondary);
}

.md-preview li { margin-bottom: 4px; }

.md-preview hr {
    border: none;
    border-top: 1px solid rgba(255,255,255,0.06);
    margin: 24px 0;
}

.md-preview img { max-width: 100%; border-radius: var(--radius-sm); }

.md-preview table {
    width: 100%;
    border-collapse: collapse;
    margin: 12px 0;
    font-size: 0.85rem;
}

.md-preview th, .md-preview td {
    padding: 8px 12px;
    border: 1px solid rgba(255,255,255,0.06);
    text-align: left;
}

.md-preview th { background: var(--glass-bg); font-weight: 600; color: var(--text-primary); }
.md-preview td { color: var(--text-secondary); }
```

- [ ] **Step 2: Commit**

```bash
git add src/Notes.Web/wwwroot/css/app.css
git commit -m "style: add markdown preview typography styles"
```

---

### Task 3: Verify with build

**Files:** none (verification only)

- [ ] **Step 1: Build the project**

```bash
nix develop --command dotnet build MarkdownMemoryNotes.sln
```

Expected: BUILD SUCCEEDED with no errors.

- [ ] **Step 2: Run tests**

```bash
nix develop --command dotnet test MarkdownMemoryNotes.sln
```

Expected: All tests pass.
