#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
comfy_dir="${TILEZ_COMFYUI_DIR:-$HOME/ComfyUI}"
export TILEZ_COMFYUI_URL="${TILEZ_COMFYUI_URL:-http://127.0.0.1:8188/}"
export TILEZ_IMAGE_BACKEND=comfyui
export TILEZ_LOCAL_CELL_SIZE="${TILEZ_LOCAL_CELL_SIZE:-128}"
export TILEZ_AI_MODEL="${TILEZ_AI_MODEL:-flux-2-klein-4b-fp8.safetensors}"
export TILEZ_COMFYUI_TEXT_ENCODER="${TILEZ_COMFYUI_TEXT_ENCODER:-qwen_3_4b.safetensors}"
export TILEZ_COMFYUI_VAE="${TILEZ_COMFYUI_VAE:-flux2-vae.safetensors}"

for model_path in \
  "$comfy_dir/models/diffusion_models/$TILEZ_AI_MODEL" \
  "$comfy_dir/models/text_encoders/$TILEZ_COMFYUI_TEXT_ENCODER" \
  "$comfy_dir/models/vae/$TILEZ_COMFYUI_VAE"; do
  if [[ ! -r "$model_path" ]]; then
    echo "Missing local model file: $model_path" >&2
    exit 2
  fi
done

service_pid=""
if ! curl --silent --fail --max-time 2 "${TILEZ_COMFYUI_URL%/}/system_stats" >/dev/null; then
  if [[ "$TILEZ_COMFYUI_URL" != "http://127.0.0.1:8188/" ]]; then
    echo "Start ComfyUI at $TILEZ_COMFYUI_URL, then rerun this launcher." >&2
    exit 2
  fi
  if [[ ! -x "$comfy_dir/venv/bin/python" ]]; then
    echo "ComfyUI Python is missing: $comfy_dir/venv/bin/python" >&2
    exit 2
  fi
  mkdir -p "$repo_root/generated/logs"
  (
    cd "$comfy_dir"
    exec "$comfy_dir/venv/bin/python" main.py --lowvram --listen 127.0.0.1 --port 8188 --disable-auto-launch
  ) >"$repo_root/generated/logs/comfyui-local.log" 2>&1 &
  service_pid="$!"
  trap 'kill -TERM "$service_pid" 2>/dev/null || true; wait "$service_pid" 2>/dev/null || true' EXIT
  for _ in {1..90}; do
    if curl --silent --fail --max-time 2 "${TILEZ_COMFYUI_URL%/}/system_stats" >/dev/null; then
      break
    fi
    if ! kill -0 "$service_pid" 2>/dev/null; then
      echo "ComfyUI exited during startup. See generated/logs/comfyui-local.log" >&2
      exit 1
    fi
    sleep 1
  done
  if ! curl --silent --fail --max-time 2 "${TILEZ_COMFYUI_URL%/}/system_stats" >/dev/null; then
    echo "ComfyUI did not become ready. See generated/logs/comfyui-local.log" >&2
    exit 1
  fi
fi

"$repo_root/run.sh" "$@"
