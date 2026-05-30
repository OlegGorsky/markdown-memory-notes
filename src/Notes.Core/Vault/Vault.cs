namespace Notes.Core.Vault;

public sealed record Vault(string RootPath)
{
    public string NotesPath => Path.Combine(RootPath, "notes");
    public string InboxPath => Path.Combine(RootPath, "inbox");
    public string MetadataPath => Path.Combine(RootPath, ".notes");
    public string SettingsPath => Path.Combine(MetadataPath, "settings.json");
    public string TrailsPath => Path.Combine(MetadataPath, "trails.json");
}
