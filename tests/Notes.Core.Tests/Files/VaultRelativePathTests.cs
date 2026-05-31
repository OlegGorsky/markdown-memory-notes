using Notes.Core.Files;
using Xunit;

namespace Notes.Core.Tests.Files;

public sealed class VaultRelativePathTests
{
    [Theory]
    [InlineData("notes/project.md", "notes/project.md")]
    [InlineData("notes/folder/idea.md", "notes/folder/idea.md")]
    [InlineData("inbox/2026-05-31.md", "inbox/2026-05-31.md")]
    [InlineData("notes\\windows\\path.md", "notes/windows/path.md")]
    public void TryNormalizeAcceptsMarkdownContentPaths(string path, string expected)
    {
        Assert.True(VaultRelativePath.TryNormalizeMarkdownContentPath(path, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/notes/project.md")]
    [InlineData("C:\\notes\\project.md")]
    [InlineData("notes/../.notes/settings.json")]
    [InlineData("notes/./project.md")]
    [InlineData(".notes/trails.json")]
    [InlineData("notes/project.txt")]
    [InlineData("media/image.png")]
    [InlineData("notes//project.md")]
    public void TryNormalizeRejectsUnsafeOrUnsupportedPaths(string path)
    {
        Assert.False(VaultRelativePath.TryNormalizeMarkdownContentPath(path, out _));
    }
}
