# Tilez Viewer

Godot viewer for the Tilez procedural tile generator. It runs offline by default.

## Run

Install Godot 4.5 or newer and the .NET 10 SDK, then from the repository root:

```bash
./run.sh
```

The launcher builds the C# generator and opens the viewer. The viewer loads exact coordinate, seed, and detail artifacts from `generated/images/`. Missing tiles are rendered by the C# CLI on a background worker. Dark squares are placeholders until their tiles finish.

Use arrow keys to pan, `+` and `-` to zoom, and `R` to retry failed tiles. At 2.5× zoom the viewer requests a higher resolution image while preserving each tile's 128-unit world footprint.

To request images from a compatible local AI service, set `TILEZ_IMAGE_BACKEND=openai`, `TILEZ_AI_BASE_URL`, and `TILEZ_AI_MODEL` before launching. The viewer uses the procedural backend when these are unset. The local AI image endpoint must support `/v1/images/generations`; a chat-only Ollama model will not generate images.

Generated output goes to the repository's `generated/` directory. The current image pipeline is a prototype: image artifacts can be regenerated, and the spatial semantic field does not yet feed render prompts.
