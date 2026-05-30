namespace Notes.Core.Files;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}
