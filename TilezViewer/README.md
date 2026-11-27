# Tilez Viewer

Godot viewer for the Tilez procedural tile world. It uses OpenRouter image generation by default.

## Run with OpenRouter

Install Godot 4.5 or newer and the .NET 10 SDK. Export an OpenRouter key in the terminal that starts the viewer, then run from the repository root:

```bash
export OPENROUTER_API_KEY=... # set this privately; do not commit it
./run.sh
```

The default model is `black-forest-labs/flux.2-klein-4b`, through OpenRouter's `/api/v1/images` endpoint. Set `TILEZ_AI_MODEL` to another Image API model that supports PNG output, 1:1/4:3/3:4 aspect ratios, reference images, and `seed` if desired. The viewer starts at most nine region jobs per batch, then pauses; press `R` to retry failures and start another batch. Set `TILEZ_MAX_RENDER_JOBS` before launch to change the batch size. Each uncached region uses one image request to fill up to nine tiles; zoom uses the same composition without another image request.

The viewer keeps region images under `generated/openrouter-regions-v1-<model-hash>/`, separate from earlier independent tiles and procedural images. A failed request leaves a placeholder and shows an error; it never silently produces a procedural tile.

Use arrow keys to pan, `+` and `-` to zoom. The viewer starts at 2.5× zoom, centered on the first region. At 2.5× zoom and above, it shows a larger texture cropped from the same region image while preserving each tile's 128-unit world footprint. Existing cached tiles use their accepted base image at both zoom levels until their region has been prepared.

## Offline mode

```bash
TILEZ_IMAGE_BACKEND=procedural ./run.sh
```

This uses the C# procedural backend and a separate `generated/procedural/` cache.

Adjacent regions include existing artwork in a reference canvas and ask the model to fill blank space. Accepted tile images are preserved. FLUX.2 Klein accepts reference images but has no placement mask, so this is reference-guided outpainting rather than a guarantee of seamless geometry. The spatial semantic field does not yet feed region prompts. OpenRouter's Batch API does not return image output; the request reduction comes from rendering several tiles in one image.
