#!/usr/bin/env bash
# Run Markdown Memory Notes desktop app
# On NixOS: uses nix develop for native dependencies
# On other Linux: runs the self-contained binary directly

set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
BINARY="$PROJECT_ROOT/publish/linux-x64/Notes.Desktop"

if [ ! -f "$BINARY" ]; then
    echo "Binary not found. Run ./publish-all.sh first."
    echo "Or: dotnet run --project src/Notes.Desktop/Notes.Desktop.csproj"
    exit 1
fi

if command -v nix &> /dev/null && [ -f "$PROJECT_ROOT/flake.nix" ]; then
    echo "Running with Nix environment..."
    cd "$PROJECT_ROOT"
    exec nix develop --command "$BINARY"
else
    exec "$BINARY"
fi
