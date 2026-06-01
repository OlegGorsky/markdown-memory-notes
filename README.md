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
MMN_SYNC_MAX_CONNECTION_ATTEMPTS_PER_MINUTE=600 \
MMN_SYNC_JOIN_TIMEOUT_SECONDS=10 \
MMN_SYNC_RECEIVE_TIMEOUT_SECONDS=120 \
MMN_SYNC_BACKPLANE_RECEIVE_QUEUE=1024 \
MMN_SYNC_TRUSTED_PROXIES=127.0.0.1 \
MMN_SYNC_TRUSTED_NETWORKS=10.0.0.0/8 \
nix develop --command dotnet run --project src/Notes.Sync/Notes.Sync.csproj
```

`MMN_SYNC_ALLOWED_ORIGINS` is optional for local development. In production, set it to a comma- or semicolon-separated list of full `http(s)://host[:port]` origins allowed to open browser WebSocket connections. When this allowlist is configured, relay connections without an `Origin` header are rejected.
`MMN_SYNC_MAX_CONNECTIONS` bounds active relay WebSockets per process, including clients that have not joined a room yet. `MMN_SYNC_MAX_CONNECTIONS_PER_CLIENT` bounds active WebSockets per observed remote client address; raise it when the relay sits behind a trusted reverse proxy that fans many users through one address. `MMN_SYNC_MAX_CONNECTION_ATTEMPTS_PER_MINUTE` limits reconnect or handshake churn per observed client address before a WebSocket slot is accepted.
`MMN_SYNC_JOIN_TIMEOUT_SECONDS` bounds how long a new WebSocket can hold a relay slot before sending its room join payload.
`MMN_SYNC_RECEIVE_TIMEOUT_SECONDS` bounds how long a joined peer can take to complete one WebSocket message. Keep it above the browser client's lightweight heartbeat interval, which defaults to roughly 45 seconds with jitter, so healthy idle sockets stay open while slow fragmented sends still cannot hold relay slots indefinitely.
`MMN_SYNC_TRUSTED_PROXIES` and `MMN_SYNC_TRUSTED_NETWORKS` enable `X-Forwarded-For` handling only for explicitly trusted reverse proxies; leave them empty when the relay is directly internet-facing.
`MMN_SYNC_BACKPLANE_REDIS` enables the optional Redis backplane for multi-instance relay deployments. Without it, each relay process only delivers messages and presence to peers connected to the same process, so a load-balanced deployment needs sticky routing for each sync room. With Redis enabled, relay instances can publish sync messages, distributed presence updates, and global room/peer admission decisions across processes behind a load balancer.
`MMN_SYNC_BACKPLANE_CHANNEL_PREFIX` isolates relay channels when several environments share Redis. `MMN_SYNC_INSTANCE_ID` should be unique per relay process; it is used to ignore messages published by the same instance.
`MMN_SYNC_BACKPLANE_RECEIVE_QUEUE` bounds queued Redis backplane messages per subscribed room before the relay starts dropping new remote payloads and increments `mmn_sync_backplane_receive_dropped_total`. Raise it for rooms with high fan-out bursts, and alert on drops because clients may need to reconnect or resync.
`MMN_SYNC_SEND_TIMEOUT_SECONDS` bounds direct peer sends, backplane publish/subscribe waits, and distributed admission/presence tracker operations. Keep it short enough that slow Redis or peers cannot hold relay cleanup paths indefinitely.
If Redis is unavailable during relay startup, the process starts in degraded mode, reports unhealthy backplane status, and retries Redis recovery with bounded backoff. When recovery succeeds, active local rooms are resubscribed to the backplane without waiting for a new peer join.
Connection limits stay per relay process and per observed client address. Room and peer limits are enforced locally without Redis, and globally with Redis enabled. Distributed admission uses expiring Redis room membership plus heartbeat renewal, so crashed relay processes age out of peer counts and capacity checks.

For a multi-instance relay deployment, add Redis backplane settings per relay process:

```bash
MMN_SYNC_BACKPLANE_REDIS=redis.internal:6379,abortConnect=false \
MMN_SYNC_BACKPLANE_CHANNEL_PREFIX=mmn:sync:prod \
MMN_SYNC_INSTANCE_ID=relay-a
```

The relay exposes `/health` and Prometheus text metrics at `/metrics`. `/health` includes `backplaneHealth`; when Redis is configured but unreachable the response body reports `status: "degraded"` and `backplaneHealthy: false`. Watch `mmn_sync_connection_rate_limited_total`, `mmn_sync_active_backplane_subscriptions`, `mmn_sync_active_send_gates`, `mmn_sync_active_backplane_receive_gates`, `mmn_sync_receive_timed_out_total`, `mmn_sync_peer_cleanup_failed_total`, `mmn_sync_backplane_publish_failed_total`, `mmn_sync_backplane_subscribe_failed_total`, `mmn_sync_backplane_invalid_payload_total`, `mmn_sync_backplane_receive_failed_total`, `mmn_sync_backplane_receive_dropped_total`, `mmn_sync_backplane_health_check_failed_total`, `mmn_sync_presence_tracker_count_failed_total`, `mmn_sync_presence_tracker_heartbeat_failed_total`, and `mmn_sync_admission_controller_failed_total` for Redis/backplane degradation, slow clients, or connection churn issues. `mmn_sync_admission_rejected_room_full_total` and `mmn_sync_admission_rejected_room_limit_total` show global capacity rejections. `mmn_sync_backplane_remote_subscribers_total` should move when messages are delivered across relay instances.

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
