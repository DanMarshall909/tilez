# Tilez Viewer - Setup Guide

This guide walks you through setting up the interactive Godot-based viewer for Tilez procedurally generated tiles.

## System Requirements

- **Windows 10+** (Windows paths used; Linux/Mac adapt as needed)
- **.NET 9.0 SDK** - For running Tilez CLI
- **Godot 4.3+** - Game engine for the viewer
- **GPU** - Recommended for Flux model (NVIDIA RTX or better)
- **8GB+ RAM** - For AI model inference
- **Disk space** - ~5GB for Flux model, ~2GB for generated tiles

## Step 1: Install Prerequisites

### A. Install .NET 9.0 SDK

1. Download from: https://dotnet.microsoft.com/download
2. Run the installer and follow prompts
3. Verify: `dotnet --version`

### B. Install Godot 4.3+

1. Download from: https://godotengine.org/download
2. Extract to a location (e.g., `C:\Program Files\Godot`)
3. Add to PATH or note the full executable path
4. Verify: `godot --version`

### C. Install & Run LMStudio or Ollama

#### Option 1: LMStudio (Recommended for Windows)

1. Download: https://lmstudio.ai/
2. Install and launch LMStudio
3. Search for "Flux" in the models search
4. Download a Flux model (e.g., `black-forest-labs/FLUX.1-dev`)
5. Click "Start Server" (default port: 1234)
6. Verify: Open http://localhost:1234/v1/models in browser

#### Option 2: Ollama

1. Download: https://ollama.ai/
2. Install and run
3. In terminal: `ollama run flux` (will download ~20GB)
4. Server runs on port 11434 by default

## Step 2: Setup Tilez Viewer Project

### A. Navigate to Project

```bash
cd C:\code\tilez\TilezViewer\project
```

### B. Open in Godot

```bash
godot
```

Or use the launcher batch file:
```bash
cd C:\code\tilez\TilezViewer
.\launch.bat
```

### C. Configure Backend (if needed)

Edit `main.gd` lines 24-28:

```gdscript
# For LMStudio (default):
var base_url: String = "http://localhost:1234"

# For Ollama:
var base_url: String = "http://localhost:11434/v1"

var ai_model: String = "flux"
var use_ai: bool = true  # Set to false for procedural generation
```

## Step 3: Run the Viewer

1. **In Godot Editor:**
   - Press `F5` to run the project
   - Or click Scene → Run

2. **Standalone:**
   - Click "Export Project" → configure export settings
   - Or run: `godot --editor path/to/project`

## Step 4: First Tile Generation

When you first run the viewer:

1. Initial screen shows "Initializing Tilez Viewer..."
2. It generates a 5x5 grid of tiles (25 tiles total)
3. **First tile takes ~30 seconds** (model warming up on GPU)
4. Subsequent tiles are faster (~5-10 seconds each)
5. Once tiles load, you'll see the message "Ready. Pan with arrows, zoom with +/-"

## Controls

| Key | Action |
|-----|--------|
| ← / → / ↑ / ↓ | Pan camera |
| `+` or `=` | Zoom in |
| `-` | Zoom out |
| `R` | Reload/reset all tiles |

## Troubleshooting

### "Could not connect to AI backend"

**Problem:** Viewer can't reach LMStudio/Ollama

**Solution:**
1. Check if LMStudio/Ollama is running
2. Verify port is correct (1234 for LMStudio, 11434 for Ollama)
3. Try in browser: `http://localhost:1234/v1/models`
4. Set `use_ai = false` to use procedural generation instead

### Tiles taking very long to generate

**Problem:** Generation timeout or GPU not being used

**Solution:**
1. Check GPU VRAM (Flux needs ~16GB)
2. In LMStudio: Verify GPU is enabled in settings
3. Monitor GPU usage: Task Manager → Performance → GPU
4. Try restarting LMStudio and reloading the model

### "Project file not found" or path errors

**Problem:** Viewer can't find Tilez executable

**Solution:**
1. Edit `main.gd` line 22:
   ```gdscript
   var dotnet_project_path: String = "C:/code/tilez/Tilez/Tilez.csproj"
   ```
   Use forward slashes and verify path exists

2. Edit `main.gd` line 23:
   ```gdscript
   var generated_images_path: String = "C:/code/tilez/generated/images"
   ```

### No tiles appear after startup

**Problem:** Generation failing silently

**Solution:**
1. Check Godot console for error messages (Ctrl+Shift+C)
2. Verify paths in `main.gd` are correct
3. Manually test tile generation:
   ```bash
   cd C:\code\tilez
   dotnet run --project Tilez/Tilez.csproj -- render --image-backend openai --ai-base-url http://localhost:1234 --ai-model flux --x 0 --y 0 --zoom 3.0 --seed 1337
   ```

## Performance Tips

1. **Zoom level affects tile count**: Higher zoom loads more tiles
2. **Reduce viewport margin** in `main.gd` line 15 to load fewer tiles
3. **Close other apps** to free GPU VRAM
4. **Use smaller Flux model** if available (though smaller = lower quality)

## Next Steps

- Explore the generated world by panning
- Modify `main.gd` to customize tile generation parameters
- Check `generated/metadata/` directory to see tile semantic features
- Integrate with web backend for multiplayer or cloud rendering

## Getting Help

- Check Godot console (Ctrl+Shift+C) for error messages
- Review console output in terminal running Tilez CLI
- Check LMStudio/Ollama logs for API issues
- See `README.md` for architecture overview
