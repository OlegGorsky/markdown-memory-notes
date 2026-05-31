using Notes.Sync;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Notes.Sync.Tests;

public sealed class SyncServerOptionsTests
{
    [Fact]
    public void DefaultsAreBoundedForPublicRelay()
    {
        var options = SyncServerOptions.Default;

        Assert.InRange(options.MaxRooms, 1, 20_000);
        Assert.InRange(options.MaxPeersPerRoom, 1, 128);
        Assert.InRange(options.MaxMessageBytes, 1, 1024 * 1024);
        Assert.InRange(options.MaxMessagesPerMinute, 1, 1_000);
        Assert.InRange(options.MaxFanoutConcurrency, 1, options.MaxPeersPerRoom);
        Assert.Empty(options.AllowedOrigins);
    }

    [Fact]
    public void FromConfigurationReadsAllowedOrigins()
    {
        var previous = Environment.GetEnvironmentVariable("MMN_SYNC_ALLOWED_ORIGINS");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_ALLOWED_ORIGINS", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:AllowedOrigins"] = "https://app.example.com, https://admin.example.com/"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(["https://app.example.com", "https://admin.example.com"], options.AllowedOrigins);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_ALLOWED_ORIGINS", previous);
        }
    }
}
