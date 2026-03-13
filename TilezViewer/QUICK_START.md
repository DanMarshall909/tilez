# Tilez Viewer - Quick Start Guide

## Prerequisites Check (30 seconds)

Before launching, ensure these are running:

```bash
# Terminal 1: Check LMStudio is running
curl http://localhost:1234/v1/models

# Should respond with something like:
# {"data": [{"id": "flux.1-schnell", ...}]}
```

If curl isn't found, just check LMStudio GUI - should have "Server Running" indicator.

## Launch Steps

### Option 1: Direct Launch
```bash
cd C:\code\tilez\TilezViewer\project
godot
```

Then press **F5** in the Godot editor to run.

### Option 2: Batch File (Windows)
```bash
cd C:\code\tilez\TilezViewer
.\launch.bat
```

Automatically opens Godot editor.

### Option 3: Debug (see all logs)
```bash
cd C:\code\tilez\TilezViewer\project
godot 2>&1 | Tee-Object -FilePath debug.log

# Then read the log:
Get-Content debug.log | Select-String "FLUX GEN|FILE SEARCH|ERROR"
```

## What to Expect

### Startup (10-15 seconds)
- Godot window opens
- Black viewport with label "Initializing Tilez Viewer..."
- Camera positioned at (0, 0)

### Initial Tile Generation (30-60 seconds)
- Viewer generates 3x3 grid (9 tiles) around origin
- **First tile: ~30 seconds** (GPU warmup)
- Subsequent tiles: ~10 seconds each
- Console shows `[FLUX GEN]` messages as it works

### Success State
- Tiles appear as colored squares in viewport
- Label shows "Ready. Use arrow keys to pan, +/- to zoom."

## Controls Once Running

| Input | Action |
|-------|--------|
| ← Arrow | Pan left |
| → Arrow | Pan right |
| ↑ Arrow | Pan up |
| ↓ Arrow | Pan down |
| `+` or `=` | Zoom in |
| `-` | Zoom out |
| `R` | Reload/reset tiles |

## Troubleshooting Quick Ref

### No tiles appear
```bash
# 1. Check LMStudio is running
curl http://localhost:1234/v1/models

# 2. Check logs for errors (look for "ERROR:" in console)

# 3. Run debug script
powershell -NoProfile -ExecutionPolicy Bypass "C:\code\tilez\TilezViewer\debug_tile_gen.ps1"

# 4. Manual generation test
cd C:\code\tilez
dotnet run --project Tilez/Tilez.csproj -- render --image-backend openai --ai-base-url http://localhost:1234 --ai-model flux.1-schnell --x 0 --y 0 --zoom 3.0 --seed 42
```

### Generation too slow
- First tile is always slow (GPU warmup)
- Close other GPU-intensive apps
- Check Task Manager → GPU usage
- Reduce viewport margin in `main.gd` line 12

### Crashes on startup
- Check Godot version: `godot --version` (should be 4.3+)
- Check .NET: `dotnet --version` (should be 9.0)
- Check absolute paths in `main.gd`:
  - Line 21: `dotnet_project_path`
  - Line 22: `generated_images_path`

## Files to Monitor

```
C:\code\tilez\
  ├── TilezViewer/
  │   ├── project/
  │   │   ├── scenes/main.gd      # Main script (check for logging)
  │   │   └── scenes/main.tscn    # Scene file
  │   ├── debug_tile_gen.ps1      # Debug script
  │   └── DEBUG_LOG.md            # Detailed troubleshooting
  ├── generated/
  │   ├── images/                 # Generated PNG tiles
  │   └── metadata/               # Tile metadata JSON
  └── Tilez/                       # C# tile generator
```

## Performance Expectations

| Action | Time | Notes |
|--------|------|-------|
| Viewer startup | 5-10s | Godot initialization |
| 1st tile generation | 25-35s | GPU warming up |
| 2nd+ tiles | 8-15s | Subsequent tiles faster |
| Pan (cached tiles) | Instant | No generation needed |
| Zoom in (new tiles) | 8-15s | Per tile generated |

## What You're Looking At

Each tile is:
- **128x128 pixels** displayed size (upscaled from 64x64)
- **256x256 high-detail** version generated when zoomed >= 2.5x
- **Procedurally influenced** by nearby tiles' semantic features (biome, mood, materials, palettes)
- **Generated via Flux** AI model with custom prompts

Tiles contain:
- Biome (e.g., "storm marsh", "crystal cavern")
- Mood (e.g., "tranquil", "ominous")
- Materials (e.g., brass, mycelium, stone)
- Motifs (e.g., pistons, roots, runes)
- Color palettes (e.g., amber, verdigris, teal)

## Next: Customization

Once it's working, you can modify in `main.gd`:

```gdscript
# Line 20: Change AI model
var ai_model: String = "flux.1-schnell"  # Change to "flux" for higher quality

# Line 23: LMStudio or Ollama?
var base_url: String = "http://localhost:1234"      # LMStudio
# var base_url: String = "http://localhost:11434/v1"  # Ollama

# Line 15: Adjust tile loading distance
var viewport_margin: int = 2  # Higher = load more tiles ahead
```

## Getting Help

1. Check console for `[FLUX GEN]` and `[ERROR]` messages
2. Read `DEBUG_LOG.md` for detailed troubleshooting
3. Run `debug_tile_gen.ps1` for system verification
4. Review `SETUP.md` for environment configuration

---

**You're ready!** Launch the viewer and explore your procedurally generated world.
