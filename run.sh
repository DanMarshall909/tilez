#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet build "$repo_root/Tilez/Tilez.csproj"
exec godot --path "$repo_root/TilezViewer/project" "$@"
