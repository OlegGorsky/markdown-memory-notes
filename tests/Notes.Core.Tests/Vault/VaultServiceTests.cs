using Notes.Core.Files;
using Notes.Core.Vault;
using Xunit;

namespace Notes.Core.Tests.Vault;

public sealed class VaultServiceTests
{
    [Fact]
    public async Task CreateVaultCreatesExpectedDirectoriesAndSettings()
    {
        var root = TestPaths.CreateTempDirectory();
        var service = new VaultService(new PhysicalFileSystem());

        var vault = await service.CreateAsync(root);

        Assert.Equal(Path.GetFullPath(root), vault.RootPath);
        Assert.True(Directory.Exists(Path.Combine(root, "notes")));
        Assert.True(Directory.Exists(Path.Combine(root, "inbox")));
        Assert.True(Directory.Exists(Path.Combine(root, ".notes")));
        Assert.True(File.Exists(Path.Combine(root, ".notes", "settings.json")));
    }

    [Fact]
    public async Task OpenVaultRejectsMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new VaultService(new PhysicalFileSystem());

        var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.OpenAsync(root));

        Assert.Contains(root, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenVaultCreatesMetadataFolderForExistingMarkdownFolder()
    {
        var root = TestPaths.CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        File.WriteAllText(Path.Combine(root, "notes", "hello.md"), "# Hello");
        var service = new VaultService(new PhysicalFileSystem());

        var vault = await service.OpenAsync(root);

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
