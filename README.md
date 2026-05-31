# Markdown Memory Notes

Local-first visual Markdown notes with quiet memory, thought trails, fragments, inbox capture, desktop UI, and CLI.

## Development on NixOS

```bash
nix develop
 dotnet restore MarkdownMemoryNotes.sln
 dotnet build MarkdownMemoryNotes.sln
 dotnet test MarkdownMemoryNotes.sln
```

## Run desktop app

```bash
nix develop --command dotnet run --project src/Notes.Desktop/Notes.Desktop.csproj
```

The desktop MVP creates a local demo vault at:

```text
~/MarkdownMemoryNotesVault
```

## Run CLI

Use `MMN_VAULT` to point the CLI at a vault:

```bash
export MMN_VAULT="$HOME/MarkdownMemoryNotesVault"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- add "Idea about quiet memory"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- find "quiet memory"
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- trail list
nix develop --command dotnet run --project src/Notes.Cli/Notes.Cli.csproj -- index rebuild
```

## Run sync relay

The web app can pair browsers through the WebSocket relay:

```bash
MMN_SYNC_URL=http://0.0.0.0:5199 \
MMN_SYNC_ALLOWED_ORIGINS=https://app.example.com \
MMN_SYNC_MAX_CONNECTIONS=20000 \
MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT=256 \
MMN_SYNC_JOIN_TIMEOUT_SECONDS=10 \
MMN_SYNC_TRUSTED_PROXIES=127.0.0.1 \
MMN_SYNC_TRUSTED_NETWORKS=10.0.0.0/8 \
nix develop --command dotnet run --project src/Notes.Sync/Notes.Sync.csproj
```

`MMN_SYNC_ALLOWED_ORIGINS` is optional for local development. In production, set it to a comma- or semicolon-separated list of full `http(s)://host[:port]` origins allowed to open browser WebSocket connections. When this allowlist is configured, relay connections without an `Origin` header are rejected.
`MMN_SYNC_MAX_CONNECTIONS` bounds active relay WebSockets per process, including clients that have not joined a room yet. `MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT` bounds active WebSockets per observed remote client address; raise it when the relay sits behind a trusted reverse proxy that fans many users through one address.
`MMN_SYNC_JOIN_TIMEOUT_SECONDS` bounds how long a new WebSocket can hold a relay slot before sending its room join payload.
`MMN_SYNC_TRUSTED_PROXIES` and `MMN_SYNC_TRUSTED_NETWORKS` enable `X-Forwarded-For` handling only for explicitly trusted reverse proxies; leave them empty when the relay is directly internet-facing.
`MMN_SYNC_BACKPLANE_REDIS` enables the optional Redis backplane for multi-instance relay deployments. Without it, each relay process only delivers messages to peers connected to the same process, so a load-balanced deployment needs sticky routing for each sync room. With Redis enabled, relay instances can publish sync messages across processes behind a load balancer.
`MMN_SYNC_BACKPLANE_CHANNEL_PREFIX` isolates relay channels when several environments share Redis. `MMN_SYNC_INSTANCE_ID` should be unique per relay process; it is used to ignore messages published by the same instance.
Connection, room, and peer limits are enforced per relay process. Presence messages currently report local process peers, not a global cross-instance count.

For a multi-instance relay deployment, add Redis backplane settings per relay process:

```bash
MMN_SYNC_BACKPLANE_REDIS=redis.internal:6379,abortConnect=false \
MMN_SYNC_BACKPLANE_CHANNEL_PREFIX=mmn:sync:prod \
MMN_SYNC_INSTANCE_ID=relay-a
```

The relay exposes `/health` and Prometheus text metrics at `/metrics`. Watch `mmn_sync_active_backplane_subscriptions`, `mmn_sync_backplane_publish_failed_total`, `mmn_sync_backplane_subscribe_failed_total`, `mmn_sync_backplane_invalid_payload_total`, and `mmn_sync_backplane_receive_failed_total` for Redis/backplane degradation. `mmn_sync_backplane_remote_subscribers_total` should move when messages are delivered across relay instances.

## Projects

- `Notes.Core`: vault, Markdown, inbox, fragments, trails, search, quiet memory.
- `Notes.Desktop`: Avalonia desktop app (Linux, macOS, Windows).
- `Notes.Cli`: command-line client over the same core.
- `Notes.Mobile`: Avalonia mobile app (Android + iOS).

## Publish & Run

```bash
# Build all desktop platforms:
./publish-all.sh

# Run desktop (auto-detects NixOS):
./run-desktop.sh

# Or run directly:
nix develop --command dotnet run --project src/Notes.Desktop/Notes.Desktop.csproj
```

Desktop outputs in `publish/<rid>/`:

| RID | Platform |
|-----|----------|
| `linux-x64` | Linux x86_64 |
| `win-x64` | Windows x64 |
| `osx-x64` | macOS Intel |
| `osx-arm64` | macOS Apple Silicon |

> On NixOS, use `nix develop` + `dotnet run`. Self-contained binaries work on standard distributions.

### Mobile

Mobile projects require platform workloads not available in the Nix .NET SDK.
Build on a machine with full .NET SDK:

```bash
# Android (needs .NET Android workload + Android SDK):
dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-android -c Release

# iOS (needs macOS + Xcode + .NET iOS workload):
dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-ios -c Release
```

## Live PWA

**[Open Memory Notes](https://oleggorsky.github.io/markdown-memory-notes/)**

Open in Chrome/Edge, click "Open vault folder" to select a local Markdown folder.
Auto-deployed from master via GitHub Actions.
