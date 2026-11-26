# Tilez Viewer

Godot viewer for the Tilez procedural tile world. It uses OpenRouter image generation by default.

## Run with OpenRouter

Install Godot 4.5 or newer and the .NET 10 SDK. Export an OpenRouter key in the terminal that starts the viewer, then run from the repository root:

```bash
export OPENROUTER_API_KEY=... # set this privately; do not commit it
./run.sh
```

The default model is `black-forest-labs/flux.2-klein-4b`, through OpenRouter's `/api/v1/images` endpoint. Set `TILEZ_AI_MODEL` to another Image API model that supports square PNG output and `seed` if desired. The viewer starts at most nine render jobs per batch, then pauses; press `R` to retry failures and authorize another batch. Set `TILEZ_MAX_RENDER_JOBS` before launch to change the batch size. A high-detail job can make two API image requests. OpenRouter may charge for each generated image.

The viewer keeps images under `generated/openrouter-<model-hash>/`, so old procedural tiles cannot masquerade as OpenRouter results. A failed request leaves a placeholder and shows an error; it never silently produces a procedural tile.

Use arrow keys to pan, `+` and `-` to zoom. At 2.5× zoom, a second image request asks for a high-detail variant while preserving each tile's 128-unit world footprint.

## Offline mode

```bash
TILEZ_IMAGE_BACKEND=procedural ./run.sh
```

This uses the C# procedural backend and a separate `generated/procedural/` cache.

The current image pipeline is a prototype. The spatial semantic field does not yet feed render prompts, and high-detail generation is a new image from text rather than an edit of the base composition.
