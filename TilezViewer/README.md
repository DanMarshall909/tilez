# Tilez Viewer

An interactive Godot-based viewer for the Tilez procedurally generated tile world system.

## Features

- **Interactive viewport** - Pan and zoom through the tile world
- **Real-time tile generation** - Generates tiles as you explore using Flux image generation
- **Seamless transitions** - Tiles blend semantic features for coherent world generation
- **Multiple backends** - Supports both AI-generated (via LMStudio/Ollama) and procedural tiles

## Requirements

- Godot 4.3+
- .NET 9.0 (for Tilez CLI)
- LMStudio or Ollama running locally with Flux model loaded
- Tilez CLI project in the parent directory

## Setup

1. **Start LMStudio or Ollama** with Flux model:
   - LMStudio: Load Flux model and start server (default: `http://localhost:1234`)
   - Ollama: `ollama run flux` (default: `http://localhost:11434/v1`)

2. **Open project in Godot**:
   ```bash
   cd TilezViewer/project
   godot
   ```

3. **Configure backend** (in `main.gd`):
   - `base_url`: Your AI backend URL
   - `ai_model`: Model name (default: `flux`)
   - `use_ai`: Set to `true` for AI generation, `false` for procedural

4. **Run the project** - Press F5 in Godot

## Controls

| Key | Action |
|-----|--------|
| Arrow Keys | Pan camera |
| `+` / `-` | Zoom in/out |
| `R` | Reload tiles |

## How It Works

1. **View area detection** - Viewer calculates which tiles are visible on screen
2. **Tile generation** - For each needed tile, calls the Tilez CLI with Flux backend
3. **Image loading** - Generated PNG is loaded as a texture and displayed
4. **Caching** - Tiles are cached in memory to avoid regenerating

## Performance Notes

- First tile generation takes ~30 seconds (depends on GPU/model)
- Subsequent tiles are faster (~5-10 seconds)
- Pan within cached tiles is instant
- Viewport margin loads extra tiles for smooth panning

## Troubleshooting

### No tiles appearing
- Check that LMStudio/Ollama is running on the configured URL
- Verify Flux model is loaded in your AI backend
- Check console for error messages

### Generation timeout
- Increase zoom level to reduce initial load time
- Check GPU VRAM is available
- LMStudio may need GPU settings adjusted

### Path errors
- Verify `dotnet_project_path` points to correct Tilez.csproj
- Verify `generated_images_path` matches Tilez output directory
- Use absolute paths if relative paths fail

## Development

The viewer is built as a simple Godot scene with GDScript. Main logic in `main.gd`:

- `_ready()` - Initialize viewer
- `_process()` - Handle input and update visible tiles
- `generate_tile_with_flux()` - Call CLI to generate tile with AI
- `generate_tile_procedural()` - Fallback procedural generation
- `display_tile()` - Render tile sprite in viewport
