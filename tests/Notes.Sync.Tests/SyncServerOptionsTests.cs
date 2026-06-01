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
        Assert.InRange(options.MaxBackplaneReceiveQueue, 1, 100_000);
        Assert.InRange(options.MaxConnections, options.MaxPeersPerRoom, 100_000);
        Assert.InRange(options.MaxConnectionsPerClient, 1, options.MaxConnections);
        Assert.InRange(options.JoinTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.InRange(options.ReceiveTimeout, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3));
        Assert.Empty(options.AllowedOrigins);
        Assert.Empty(options.TrustedProxies);
        Assert.Empty(options.TrustedNetworks);
        Assert.Null(options.BackplaneRedisConnectionString);
        Assert.False(string.IsNullOrWhiteSpace(options.BackplaneChannelPrefix));
        Assert.False(string.IsNullOrWhiteSpace(options.InstanceId));
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

    [Fact]
    public void FromConfigurationReadsConnectionLimits()
    {
        var previousMaxConnections = Environment.GetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS");
        var previousMaxConnectionsPerClient = Environment.GetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS", null);
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:MaxConnections"] = "2500",
                    ["Sync:MaxConnectionsPerClient"] = "75"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(2500, options.MaxConnections);
            Assert.Equal(75, options.MaxConnectionsPerClient);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS", previousMaxConnections);
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT", previousMaxConnectionsPerClient);
        }
    }

    [Fact]
    public void FromConfigurationAllowsSmallConnectionLimitForConstrainedInstances()
    {
        var previousMaxConnections = Environment.GetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:MaxConnections"] = "1"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(1, options.MaxConnections);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_MAX_CONNECTIONS", previousMaxConnections);
        }
    }

    [Fact]
    public void FromConfigurationReadsJoinTimeout()
    {
        var previous = Environment.GetEnvironmentVariable("MMN_SYNC_JOIN_TIMEOUT_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_JOIN_TIMEOUT_SECONDS", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:JoinTimeoutSeconds"] = "3"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(TimeSpan.FromSeconds(3), options.JoinTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_JOIN_TIMEOUT_SECONDS", previous);
        }
    }

    [Fact]
    public void FromConfigurationReadsReceiveTimeout()
    {
        var previous = Environment.GetEnvironmentVariable("MMN_SYNC_RECEIVE_TIMEOUT_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_RECEIVE_TIMEOUT_SECONDS", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:ReceiveTimeoutSeconds"] = "7"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(TimeSpan.FromSeconds(7), options.ReceiveTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_RECEIVE_TIMEOUT_SECONDS", previous);
        }
    }

    [Fact]
    public void FromConfigurationReadsTrustedForwardedHeaderSources()
    {
        var previousProxies = Environment.GetEnvironmentVariable("MMN_SYNC_TRUSTED_PROXIES");
        var previousNetworks = Environment.GetEnvironmentVariable("MMN_SYNC_TRUSTED_NETWORKS");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_TRUSTED_PROXIES", null);
            Environment.SetEnvironmentVariable("MMN_SYNC_TRUSTED_NETWORKS", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:TrustedProxies"] = "127.0.0.1; 10.0.0.5",
                    ["Sync:TrustedNetworks"] = "10.10.0.0/16, 2001:db8::/32"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal(["127.0.0.1", "10.0.0.5"], options.TrustedProxies);
            Assert.Equal(["10.10.0.0/16", "2001:db8::/32"], options.TrustedNetworks);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_TRUSTED_PROXIES", previousProxies);
            Environment.SetEnvironmentVariable("MMN_SYNC_TRUSTED_NETWORKS", previousNetworks);
        }
    }

    [Fact]
    public void FromConfigurationReadsBackplaneSettings()
    {
        var previousRedis = Environment.GetEnvironmentVariable("MMN_SYNC_BACKPLANE_REDIS");
        var previousPrefix = Environment.GetEnvironmentVariable("MMN_SYNC_BACKPLANE_CHANNEL_PREFIX");
        var previousInstance = Environment.GetEnvironmentVariable("MMN_SYNC_INSTANCE_ID");
        var previousQueue = Environment.GetEnvironmentVariable("MMN_SYNC_BACKPLANE_RECEIVE_QUEUE");
        try
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_REDIS", null);
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_CHANNEL_PREFIX", null);
            Environment.SetEnvironmentVariable("MMN_SYNC_INSTANCE_ID", null);
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_RECEIVE_QUEUE", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:BackplaneRedis"] = "localhost:6379,abortConnect=false",
                    ["Sync:BackplaneChannelPrefix"] = "notes-prod",
                    ["Sync:InstanceId"] = "relay-a",
                    ["Sync:MaxBackplaneReceiveQueue"] = "2048"
                })
                .Build();

            var options = SyncServerOptions.FromConfiguration(configuration);

            Assert.Equal("localhost:6379,abortConnect=false", options.BackplaneRedisConnectionString);
            Assert.Equal("notes-prod", options.BackplaneChannelPrefix);
            Assert.Equal("relay-a", options.InstanceId);
            Assert.Equal(2048, options.MaxBackplaneReceiveQueue);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_REDIS", previousRedis);
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_CHANNEL_PREFIX", previousPrefix);
            Environment.SetEnvironmentVariable("MMN_SYNC_INSTANCE_ID", previousInstance);
            Environment.SetEnvironmentVariable("MMN_SYNC_BACKPLANE_RECEIVE_QUEUE", previousQueue);
        }
    }
}
