using System.Collections;
using System.Reflection;
using MemoryNotes.Web.Services;
using Microsoft.JSInterop;
using Notes.Core.Notes;
using Xunit;
using NotesPage = MemoryNotes.Web.Pages.Notes;

namespace Notes.Web.Tests.Pages;

public sealed class NotesTreeTests
{
    [Fact]
    public async Task BuildTreeKeepsOnlyTopLevelFoldersExpandedByDefault()
    {
        using var page = new NotesPage();
        await using var fileSystem = new BrowserFileSystem(new NoopJsRuntime());
        SetSession(page, new WebVaultSession(fileSystem));
        var now = DateTimeOffset.Now;
        var notes = new List<Note>
        {
            new("note_a", "A", "notes/projects/a.md", "body", now, now),
            new("note_b", "B", "notes/projects/nested/b.md", "body", now, now)
        };

        var tree = BuildTree(page, notes);

        var notesRoot = Assert.Single(tree);
        Assert.Equal("notes", notesRoot.Name);
        Assert.True(notesRoot.Expanded);

        var projectsFolder = Assert.Single(notesRoot.Children);
        Assert.Equal("projects", projectsFolder.Name);
        Assert.False(projectsFolder.Expanded);
    }

    [Fact]
    public async Task BuildTreeBoundsInitiallyVisibleChildrenForLargeFolders()
    {
        using var page = new NotesPage();
        await using var fileSystem = new BrowserFileSystem(new NoopJsRuntime());
        SetSession(page, new WebVaultSession(fileSystem));
        var now = DateTimeOffset.Now;
        var notes = Enumerable.Range(0, 240)
            .Select(index => new Note(
                $"note_{index}",
                $"Note {index}",
                $"notes/note-{index:000}.md",
                "body",
                now,
                now))
            .ToList();

        var tree = BuildTree(page, notes);

        var notesRoot = Assert.Single(tree);
        var visibleChildren = GetVisibleChildren(notesRoot);
        Assert.True(visibleChildren.Length < notesRoot.Children.Count);
        Assert.Equal(80, visibleChildren.Length);

        ShowMoreChildren(notesRoot);

        Assert.Equal(160, GetVisibleChildren(notesRoot).Length);
    }

    [Fact]
    public async Task BuildTreePreservesExpandedAndRevealedTreeStateAcrossRebuilds()
    {
        using var page = new NotesPage();
        await using var fileSystem = new BrowserFileSystem(new NoopJsRuntime());
        SetSession(page, new WebVaultSession(fileSystem));
        var now = DateTimeOffset.Now;
        var notes = Enumerable.Range(0, 240)
            .Select(index => new Note(
                $"note_{index}",
                $"Note {index}",
                $"notes/projects/note-{index:000}.md",
                "body",
                now,
                now))
            .ToList();

        var initialTree = BuildTree(page, notes);
        var notesRoot = Assert.Single(initialTree);
        var projectsFolder = Assert.Single(notesRoot.Children);
        projectsFolder.Expanded = true;
        projectsFolder.ShowMoreChildren();
        SetField(page, "_noteTree", initialTree);

        var rebuiltTree = BuildTree(page, notes);

        var rebuiltRoot = Assert.Single(rebuiltTree);
        var rebuiltProjectsFolder = Assert.Single(rebuiltRoot.Children);
        Assert.True(rebuiltProjectsFolder.Expanded);
        Assert.Equal(160, GetVisibleChildren(rebuiltProjectsFolder).Length);
    }

    private static List<NotesPage.TreeNode> BuildTree(NotesPage page, List<Note> notes)
    {
        var method = typeof(NotesPage).GetMethod("BuildTree", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildTree method was not found.");

        return (List<NotesPage.TreeNode>)(method.Invoke(page, [notes])
            ?? throw new InvalidOperationException("BuildTree returned null."));
    }

    private static NotesPage.TreeNode[] GetVisibleChildren(NotesPage.TreeNode node)
    {
        var property = typeof(NotesPage.TreeNode).GetProperty("VisibleChildren", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("VisibleChildren property was not found.");

        return ((IEnumerable)(property.GetValue(node)
                ?? throw new InvalidOperationException("VisibleChildren returned null.")))
            .Cast<NotesPage.TreeNode>()
            .ToArray();
    }

    private static void ShowMoreChildren(NotesPage.TreeNode node)
    {
        var method = typeof(NotesPage.TreeNode).GetMethod("ShowMoreChildren", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("ShowMoreChildren method was not found.");

        method.Invoke(node, null);
    }

    private static void SetSession(NotesPage page, WebVaultSession session)
    {
        var property = typeof(NotesPage).GetProperty("Session", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Session property was not found.");

        property.SetValue(page, session);
    }

    private static void SetField<T>(NotesPage page, string name, T value)
    {
        var field = typeof(NotesPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} field was not found.");

        field.SetValue(page, value);
    }

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            throw new NotSupportedException("JS interop is not used by this test.");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            throw new NotSupportedException("JS interop is not used by this test.");
        }
    }
}
