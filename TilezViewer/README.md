# Tilez Viewer

Godot viewer for the Tilez procedural tile world. It uses OpenRouter image generation by default.

## Run on the local GPU

On this machine, ComfyUI and the FLUX.2 Klein 4B FP8 model are installed. From the repository root:

```bash
./run-local.sh
```

The launcher starts ComfyUI in low VRAM mode if it is not already running, then opens the viewer with `TILEZ_IMAGE_BACKEND=comfyui`. No OpenRouter key or image API charge is involved. It generates one 3×3 region at a time by default; press `R` to start another batch. Set `TILEZ_MAX_RENDER_JOBS` to change that limit. Local generation defaults to 192 source pixels per tile, making a first region 576×576 pixels. Set `TILEZ_LOCAL_CELL_SIZE=128` for smaller, faster images or `256` for more detail. Zoom textures are resized from the same region image. The local cache, including the resolution setting in its identity, is separate at `generated/comfyui-regions-v4-<settings-hash>/`.

The required model files are `flux-2-klein-4b-fp8.safetensors` in `~/ComfyUI/models/diffusion_models/`, `qwen_3_4b.safetensors` in `~/ComfyUI/models/text_encoders/`, and `flux2-vae.safetensors` in `~/ComfyUI/models/vae/`. The launcher accepts `TILEZ_COMFYUI_DIR` and `TILEZ_COMFYUI_URL` for another local installation. See the [FLUX.2 Klein FP8 model](https://huggingface.co/black-forest-labs/FLUX.2-klein-4b-fp8) and [ComfyUI model layout](https://huggingface.co/Comfy-Org/Flux-2-Klein-4B).

For expansion, Tilez sends the accepted map plus an explicit mask of the blank cells to ComfyUI. The local workflow denoises the masked area and preserves already accepted tile files. It crops a small outer margin to remove frames the model may draw. Roads and objects can still change direction at a region boundary; the mask fixes the area being edited, not the scene geometry.

## Run with OpenRouter

Install Godot 4.5 or newer and the .NET 10 SDK. Export an OpenRouter key in the terminal that starts the viewer, then run from the repository root:

```bash
export OPENROUTER_API_KEY=... # set this privately; do not commit it
./run.sh
```

The default model is `black-forest-labs/flux.2-klein-4b`, through OpenRouter's `/api/v1/images` endpoint. Set `TILEZ_AI_MODEL` to another Image API model that supports PNG output, 1:1/4:3/3:4 aspect ratios, reference images, and `seed` if desired. The viewer starts at most nine region jobs per batch, then pauses; press `R` to retry failures and start another batch. Set `TILEZ_MAX_RENDER_JOBS` before launch to change the batch size. Each uncached region uses one image request to fill up to nine tiles; zoom uses the same composition without another image request.

The viewer keeps region images under `generated/openrouter-regions-v3-<model-hash>/`, separate from earlier independent tiles and procedural images. A failed request leaves a placeholder and shows an error; it never silently produces a procedural tile.

Use arrow keys to pan, `+` and `-` to zoom. The viewer starts at 2.5× zoom, centered on the first region. At 2.5× zoom and above, it shows a larger texture cropped from the same region image while preserving each tile's 128-unit world footprint. Existing cached tiles use their accepted base image at both zoom levels until their region has been prepared.

## Offline mode

```bash
TILEZ_IMAGE_BACKEND=procedural ./run.sh
```

This uses the C# procedural backend and a separate `generated/procedural/` cache.

OpenRouter expansion uses the existing artwork as a reference and preserves accepted tile files, but its image endpoint has no placement mask. The local ComfyUI backend adds a denoising mask for the blank area. The spatial semantic field does not yet feed region prompts. OpenRouter's Batch API does not return image output; the request reduction comes from rendering several tiles in one image.
