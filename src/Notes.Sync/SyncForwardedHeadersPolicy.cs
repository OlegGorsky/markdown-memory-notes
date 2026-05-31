using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using NetIPNetwork = System.Net.IPNetwork;

namespace Notes.Sync;

public static class SyncForwardedHeadersPolicy
{
    public static bool IsConfigured(SyncServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.TrustedProxies.Any(static proxy => IPAddress.TryParse(proxy, out _)) ||
               options.TrustedNetworks.Any(static network => TryParseNetwork(network, out _));
    }

    public static ForwardedHeadersOptions Create(SyncServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();

        foreach (var proxy in options.TrustedProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                forwarded.KnownProxies.Add(address);
            }
        }

        foreach (var network in options.TrustedNetworks)
        {
            if (TryParseNetwork(network, out var parsedNetwork))
            {
                forwarded.KnownIPNetworks.Add(parsedNetwork);
            }
        }

        return forwarded;
    }

    private static bool TryParseNetwork(string value, out NetIPNetwork network)
    {
        network = default!;
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefix) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var maxPrefixLength = prefix.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            return false;
        }

        network = new NetIPNetwork(prefix, prefixLength);
        return true;
    }
}
