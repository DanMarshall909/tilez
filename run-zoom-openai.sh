#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TILEZ_IMAGE_BACKEND=openai-zoom
export TILEZ_ZOOM_BACKEND=openai-zoom
export TILEZ_OPENAI_IMAGE_MODEL="${TILEZ_OPENAI_IMAGE_MODEL:-gpt-image-2.6-sunburst}"
export TILEZ_OPENAI_IMAGE_QUALITY="${TILEZ_OPENAI_IMAGE_QUALITY:-medium}"
exec "$repo_root/run.sh" res://scenes/zoom.tscn
