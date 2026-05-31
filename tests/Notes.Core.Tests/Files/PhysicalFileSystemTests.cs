using Notes.Core.Files;
using Xunit;

namespace Notes.Core.Tests.Files;

public sealed class PhysicalFileSystemTests
{
    [Fact]
    public async Task DeleteFileRemovesExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "note.md");
        await File.WriteAllTextAsync(path, "# Note", TestContext.Current.CancellationToken);
        var fileSystem = new PhysicalFileSystem();

        await fileSystem.DeleteFileAsync(path);

        Assert.False(File.Exists(path));
    }
}
