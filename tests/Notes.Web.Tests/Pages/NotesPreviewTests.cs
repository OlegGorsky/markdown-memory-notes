using System.Reflection;
using Xunit;
using NotesPage = MemoryNotes.Web.Pages.Notes;

namespace Notes.Web.Tests.Pages;

public sealed class NotesPreviewTests
{
    [Fact]
    public void RenderPreviewEscapesRawHtmlAndRemovesUnsafeLinks()
    {
        var html = RenderPreview("""
            # Safe heading

            <script>alert('xss')</script>

            Hello <img src=x onerror=alert('xss')>

            [unsafe](javascript:alert('xss'))

            [safe](https://example.com/docs)
            """);

        Assert.Contains("<h1>Safe heading</h1>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.com/docs\"", html, StringComparison.Ordinal);
    }

    private static string RenderPreview(string markdown)
    {
        using var page = new NotesPage();
        var editBodyField = typeof(NotesPage).GetField("_editBody", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_editBody field was not found.");
        var renderPreviewMethod = typeof(NotesPage).GetMethod("RenderPreview", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RenderPreview method was not found.");

        editBodyField.SetValue(page, markdown);
        return (string)(renderPreviewMethod.Invoke(page, null)
            ?? throw new InvalidOperationException("RenderPreview returned null."));
    }
}
