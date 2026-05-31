namespace Notes.Core.Files;

public interface IFileSystem
{
    Task<bool> DirectoryExistsAsync(string path);
    Task<bool> FileExistsAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string contents);
    Task DeleteFileAsync(string path);
    Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption);
}
