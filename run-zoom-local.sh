#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TILEZ_ZOOM_BACKEND=comfyui
exec "$repo_root/run-local.sh" res://scenes/zoom.tscn
