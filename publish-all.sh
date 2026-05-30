#!/usr/bin/env bash
# Markdown Memory Notes — publish for all platforms
# Usage: ./publish-all.sh
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$PROJECT_ROOT"

echo "=== Publishing Markdown Memory Notes ==="

# Desktop (self-contained)
echo ""
echo "--- Desktop ---"
for rid in linux-x64 win-x64 osx-x64 osx-arm64; do
    echo "  $rid..."
    dotnet publish src/Notes.Desktop/Notes.Desktop.csproj \
        -r "$rid" --self-contained true -c Release \
        -o "publish/$rid" \
        /p:DebugType=None /p:DebugSymbols=false \
        > /dev/null 2>&1
    echo "  Done: publish/$rid/"
done

echo ""
echo "Desktop builds:"
for rid in linux-x64 win-x64 osx-x64 osx-arm64; do
    size=$(du -sh "publish/$rid" 2>/dev/null | cut -f1)
    echo "  $rid  ($size)"
done

# Mobile (requires platform workloads)
echo ""
echo "--- Mobile ---"
echo "  Mobile targets require additional .NET workloads."
echo ""
echo "  Android (needs .NET Android workload + Android SDK):"
echo "    dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-android -c Release"
echo ""
echo "  iOS (needs macOS + Xcode + .NET iOS workload):"
echo "    dotnet publish src/Notes.Mobile/Notes.Mobile.csproj -f net10.0-ios -c Release"

echo ""
echo "=== Publish complete ==="
echo ""
echo "Run:"
echo "  ./run-desktop.sh           (Linux, auto-detects NixOS)"
echo "  publish/win-x64/Notes.Desktop.exe   (Windows)"
echo "  publish/osx-arm64/Notes.Desktop     (macOS Apple Silicon)"

