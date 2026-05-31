namespace Notes.Core.Files;

public sealed class PhysicalFileSystem : IFileSystem
{
    public Task<bool> DirectoryExistsAsync(string path)
        => Task.FromResult(Directory.Exists(path));

    public Task<bool> FileExistsAsync(string path)
        => Task.FromResult(File.Exists(path));

    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task<string> ReadAllTextAsync(string path)
        => Task.FromResult(File.ReadAllText(path));

    public Task WriteAllTextAsync(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
        => Task.FromResult(Directory.EnumerateFiles(path, searchPattern, searchOption));
}
