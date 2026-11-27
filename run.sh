#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TILEZ_IMAGE_BACKEND="$(printf '%s' "${TILEZ_IMAGE_BACKEND:-openrouter}" | tr '[:upper:]' '[:lower:]')"
case "$TILEZ_IMAGE_BACKEND" in
  openrouter|comfyui|openai|openai-zoom|openrouter-zoom|ai|procedural) ;;
  *) echo "Unsupported image backend: $TILEZ_IMAGE_BACKEND" >&2; exit 2 ;;
esac
if [[ "$TILEZ_IMAGE_BACKEND" == "openrouter" && -z "${OPENROUTER_API_KEY:-}" ]]; then
  echo "OPENROUTER_API_KEY is required. Export it, or run TILEZ_IMAGE_BACKEND=procedural ./run.sh for offline tiles." >&2
  exit 2
fi
if [[ "$TILEZ_IMAGE_BACKEND" == "openrouter-zoom" && -z "${OPENROUTER_API_KEY:-}" ]]; then
  echo "OPENROUTER_API_KEY is required for the OpenAI model through OpenRouter." >&2
  exit 2
fi
if [[ "$TILEZ_IMAGE_BACKEND" == "openai-zoom" && -z "${OPENAI_API_KEY:-}" ]]; then
  echo "OPENAI_API_KEY is required for direct OpenAI zoom generation." >&2
  exit 2
fi
dotnet build "$repo_root/Tilez/Tilez.csproj"
exec godot --path "$repo_root/TilezViewer/project" "$@"
