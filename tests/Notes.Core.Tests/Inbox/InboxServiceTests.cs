using Notes.Core.Files;
using Notes.Core.Inbox;
using Xunit;

namespace Notes.Core.Tests.Inbox;

public sealed class InboxServiceTests
{
    [Fact]
    public void CaptureAppendsToTodayInboxNote()
    {
        var vault = CreateVault();
        var service = new InboxService(new PhysicalFileSystem(), () => new DateTimeOffset(2026, 5, 30, 14, 15, 0, TimeSpan.FromHours(3)));

        service.Capture(vault, "Idea about quiet memory");
        service.Capture(vault, "Second thought");

        var path = Path.Combine(vault.InboxPath, "2026-05-30.md");
        var text = File.ReadAllText(path);
        Assert.Contains("# Inbox 2026-05-30", text, StringComparison.Ordinal);
        Assert.Contains("- 14:15 Idea about quiet memory", text, StringComparison.Ordinal);
        Assert.Contains("- 14:15 Second thought", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureRejectsBlankText()
    {
        var vault = CreateVault();
        var service = new InboxService(new PhysicalFileSystem(), () => DateTimeOffset.Now);

        var exception = Assert.Throws<ArgumentException>(() => service.Capture(vault, "   "));

        Assert.Contains("Inbox text cannot be empty", exception.Message, StringComparison.Ordinal);
    }

    private static global::Notes.Core.Vault.Vault CreateVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmn-tests", Guid.NewGuid().ToString("N"));
        return new global::Notes.Core.Vault.VaultService(new PhysicalFileSystem()).Create(root);
    }
}
