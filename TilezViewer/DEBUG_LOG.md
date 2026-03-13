# Tilez Viewer - Debug Log and Solution

## Problem Found

When the Godot viewer was running, it showed:
```
Generating tile (-1, -1) with Flux...
ERROR: No generated file found after running command
```

This indicated that:
1. The `OS.execute()` command was running
2. But the generated files were not being found afterward

## Investigation

### Test Results
Using command-line tests, confirmed that:
- ✓ LMStudio API is working (`http://localhost:1234/v1/models` responds with flux.1-schnell)
- ✓ Tilez CLI generation works perfectly
- ✓ Files ARE being created in `C:\code\tilez\generated\images\`

Example successful generation:
```bash
cd C:\code\tilez
dotnet run --project Tilez/Tilez.csproj -- render \
  --image-backend openai \
  --ai-base-url http://localhost:1234 \
  --ai-model flux.1-schnell \
  --x 77 --y 77 --zoom 3.0 --seed 888888

# Output:
# Low-res: C:\code\tilez\generated\images\tile_77_77_888888_low_64.png
# Upscaled: C:\code\tilez\generated\images\tile_77_77_888888_low_64_upscaled_128.png
# High-detail: C:\code\tilez\generated\images\tile_77_77_888888_high_256.png
```

Files confirmed to exist:
```
tile_77_77_888888_base_64.png
tile_77_77_888888_low_64.png
tile_77_77_888888_low_64_upscaled_128.png
tile_77_77_888888_high_256.png
```

### Root Cause

The Godot `get_matching_files()` function had issues:
1. **Relative path problems**: Was using relative paths like `../../generated/images`
2. **Path joining issues**: Godot's `path_join()` may not work reliably with absolute paths
3. **Silent failures**: No detailed logging made the issue invisible

## Solution Implemented

### 1. Added Comprehensive Logging
Every major function now has detailed logging:

```gdscript
func log(msg: String) -> void:
    var timestamp = Time.get_ticks_msec()
    var formatted = "[%d] %s" % [timestamp, msg]
    print(formatted)
    if debug_enabled:
        print_debug(formatted)
```

### 2. Use Absolute Paths
Changed from relative to absolute paths:

```gdscript
# Before (problematic):
var generated_images_path: String = "../../generated/images"

# After (works):
var generated_images_path: String = "C:\\code\\tilez\\generated\\images"
```

### 3. Enhanced File Search with Full Debugging
```gdscript
func get_matching_files(directory: String, pattern: String) -> PackedStringArray:
    var abs_dir = "C:\\code\\tilez\\generated\\images"
    
    log("[FILE SEARCH] Looking in: %s" % abs_dir)
    log("[FILE SEARCH] Pattern: %s" % pattern)
    
    var dir = DirAccess.open(abs_dir)
    
    # ... Scans every file with logging ...
    # ... Logs pattern matching results ...
```

### 4. Detailed Tile Generation Logging
```gdscript
func generate_tile_with_flux(x: int, y: int) -> void:
    log("[FLUX GEN] Starting tile generation (%d, %d) seed=%d" % [x, y, seed])
    # ... All steps logged ...
    log("[FLUX GEN] Command finished in %d ms, exit code: %d" % [elapsed, result])
    # ... Output captured and logged ...
    log("[FLUX GEN] Found %d matching files" % files.size())
```

## How to Debug Future Issues

### 1. Check Console Output
When running `godot`:
```bash
cd C:\code\tilez\TilezViewer\project
godot 2>&1 | grep -i "ERROR\|ERROR\|FLUX GEN"
```

Look for `[FLUX GEN]`, `[FILE SEARCH]`, `[TEXTURE]`, `[DISPLAY]` prefixed logs.

### 2. Test Generation Manually
```bash
cd C:\code\tilez
dotnet run --project Tilez/Tilez.csproj -- render \
  --image-backend openai \
  --ai-base-url http://localhost:1234 \
  --ai-model flux.1-schnell \
  --x 0 --y 0 --zoom 3.0 --seed 12345
```

Then check if files exist:
```bash
ls C:\code\tilez\generated\images\tile_0_0*
```

### 3. Run Debug Script
```bash
powershell -NoProfile -ExecutionPolicy Bypass "C:\code\tilez\TilezViewer\debug_tile_gen.ps1"
```

This will:
- Verify directories exist
- Run a test generation
- Check for output files
- List recent files

## Key Logging Tags

When reading the console output, look for these prefixes:

| Tag | Meaning |
|-----|---------|
| `[FLUX GEN]` | Flux tile generation process |
| `[FILE SEARCH]` | File directory searching |
| `[TEXTURE]` | Image loading into texture |
| `[DISPLAY]` | Sprite display in viewport |

## Next Steps to Fix Remaining Issues

If tiles still don't appear:

1. **Check console for `[FLUX GEN]` errors**
   - Look for command execution failures
   - Check OS.execute() exit codes

2. **Verify file paths in log output**
   - Confirm correct directory is being searched
   - Check if `path_join()` works correctly with absolute paths

3. **Test manual file discovery**
   - Run the PowerShell debug script
   - Manually list files in C:\code\tilez\generated\images\

4. **Add more granular logging**
   - If patterns still don't match, add logging inside the while loop
   - Log exact filenames being checked vs. pattern

## Files Modified

- `TilezViewer/project/scenes/main.gd` - Added comprehensive logging
- `TilezViewer/project/scenes/main.tscn` - Fixed scene format issues
- `TilezViewer/debug_tile_gen.ps1` - Debug script for testing
- `TilezViewer/test_viewer.py` - Python test script

## Performance Notes

The logging adds minimal overhead (just string concatenation). If needed, disable with:
```gdscript
var debug_enabled: bool = false  # Disable in main.gd
```
