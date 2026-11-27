# Tilez Viewer

Godot viewer for the Tilez procedural tile world. It uses OpenRouter image generation by default.

## Predictive center zoom (separate viewer)

From the repository root, run `./run-zoom-local.sh`. This opens a separate Godot scene and uses the local ComfyUI model. The original map viewer still starts with `./run-local.sh`. The center zoom stores accepted frames in `generated/center-zoom-v2/seed_1337/frames/` and reuses them after a restart. It keeps generating until the viewer is closed. Set `TILEZ_ZOOM_MAX_DEPTH` to impose an optional depth cap. Delete that output directory if you want a new sequence from the first frame.

Each frame is generated at 512×512. The viewer shows a centered 256×256 window with nearest-neighbor texture filtering and an integer screen scale. Each step zooms about 1.25×, retaining a 410×410 center crop of the previous frame as the next image-to-image reference. It prepares the next frame before zooming, then starts the following generation while the current transition plays. The transition duration follows the measured generation time, with a small margin; if generation takes longer, the viewer holds the current image. The last part of each transition blends into the prepared frame. The model adds finer detail to the enlarged center crop, though composition is still model-dependent.

## Run on the local GPU

On this machine, ComfyUI and the FLUX.2 Klein 4B FP8 model are installed. From the repository root:

```bash
./run-local.sh
```

The launcher starts ComfyUI in low VRAM mode if it is not already running, then opens the viewer with `TILEZ_IMAGE_BACKEND=comfyui`. No OpenRouter key or image API charge is involved. It generates one 3×3 region at a time and continues through the visible area automatically; press `R` to retry failures. Local generation defaults to 128 source pixels per tile, making a first region 384×384 pixels. Set `TILEZ_LOCAL_CELL_SIZE=192` or `256` for more detail. Zoom textures are resized from the same region image. The local cache, including the resolution setting in its identity, is separate at `generated/comfyui-regions-v6-<settings-hash>/`.

The required model files are `flux-2-klein-4b-fp8.safetensors` in `~/ComfyUI/models/diffusion_models/`, `qwen_3_4b.safetensors` in `~/ComfyUI/models/text_encoders/`, and `flux2-vae.safetensors` in `~/ComfyUI/models/vae/`. The launcher accepts `TILEZ_COMFYUI_DIR` and `TILEZ_COMFYUI_URL` for another local installation. See the [FLUX.2 Klein FP8 model](https://huggingface.co/black-forest-labs/FLUX.2-klein-4b-fp8) and [ComfyUI model layout](https://huggingface.co/Comfy-Org/Flux-2-Klein-4B).

For expansion, Tilez sends the accepted map plus an explicit mask of the blank cells to ComfyUI. The local workflow denoises the masked area and preserves already accepted tile files. It crops a small outer margin only on the first unreferenced region, so expansions retain the mask's exact coordinates. Region prompts use existing authored metadata or deterministic generated features: each position contributes its biome, while the region centre sets the shared mood, motifs, materials and colours. Roads and objects can still change direction at a region boundary; the mask fixes the area being edited, not the scene geometry. The current model can interpret varied biomes as enclosed scenes even when prompted for continuous terrain.

## Run with OpenRouter

Install Godot 4.5 or newer and the .NET 10 SDK. Export an OpenRouter key in the terminal that starts the viewer, then run from the repository root:

```bash
export OPENROUTER_API_KEY=... # set this privately; do not commit it
./run.sh
```

The default model is `black-forest-labs/flux.2-klein-4b`, through OpenRouter's `/api/v1/images` endpoint. Set `TILEZ_AI_MODEL` to another Image API model that supports PNG output, 1:1/4:3/3:4 aspect ratios, reference images, and `seed` if desired. The viewer starts at most nine region jobs per batch, then pauses; press `R` to retry failures and start another batch. Set `TILEZ_MAX_RENDER_JOBS` before launch to change the batch size. Each uncached region uses one image request to fill up to nine tiles; zoom uses the same composition without another image request.

The viewer keeps region images under `generated/openrouter-regions-v5-<model-hash>/`, separate from earlier independent tiles and procedural images. A failed request leaves a placeholder and shows an error; it never silently produces a procedural tile.

Use arrow keys to pan, `+` and `-` to zoom. The viewer starts at 3× zoom, centered on the first region. At 2.5× zoom and above, it shows a larger texture cropped from the same region image while preserving each tile's 128-unit world footprint. Existing cached tiles use their accepted base image at both zoom levels until their region has been prepared.

## Offline mode

```bash
TILEZ_IMAGE_BACKEND=procedural ./run.sh
```

This uses the C# procedural backend and a separate `generated/procedural/` cache.

OpenRouter expansion uses the existing artwork as a reference and preserves accepted tile files, but its image endpoint has no placement mask. The local ComfyUI backend adds a denoising mask for the blank area. Region prompts now contain per-position semantic features; the distance-weighted semantic field does not yet drive those prompts. OpenRouter's Batch API does not return image output; the request reduction comes from rendering several tiles in one image.
