# note.gorseecode.ru deployment

This folder contains the isolated Docker deployment for Markdown Memory Notes on
`note.gorseecode.ru`.

## Services

- `note-web`: Blazor WebAssembly static files served by an internal Caddy.
- `note-sync`: ASP.NET Core WebSocket relay on `/sync`.
- `note-redis`: isolated Redis backplane for sync presence/admission/pub-sub.

The stack does not publish host ports. The public ingress Caddy is expected to
join the `markdown-memory-notes_note` network and proxy `note.gorseecode.ru` to
`note-web:8080` and `note-sync:5199`.

## Deploy

```bash
docker compose -f deploy/note/docker-compose.yml up -d --build
```

Then add `deploy/note/caddy.note.gorseecode.ru.conf` to the public Caddyfile and
reload Caddy.
