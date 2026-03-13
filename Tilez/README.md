# Tilez Prototype

Phase 1-2 prototype for deterministic tile generation and multi-tile painting.

## Commands

- Single tile (default):

```bash
dotnet run -- --x 0 --y 0 --seed 1337
```

- Paint a rectangle of tiles:

```bash
dotnet run -- paint --min-x 0 --max-x 2 --min-y 0 --max-y 2 --seed 1337
```

- Author tiles from a JSON file:

```bash
dotnet run -- author --file authoring.json
```

- Prefetch viewport + offscreen margin:

```bash
dotnet run -- prefetch --viewport-min-x 0 --viewport-max-x 2 --viewport-min-y 0 --viewport-max-y 2 --margin 1 --seed 1337
```

- Sample location-sensitive prompt influence:

```bash
dotnet run -- sample --x 0.5 --y 0.5 --seed 1337
```

- Simulate camera panning with visibility-priority generation:

```bash
dotnet run -- pan --start-x 0 --start-y 0 --end-x 5 --end-y 0 --viewport-width 3 --viewport-height 3 --steps 10 --seed 1337
```

- Sample prompt field at a zoom level (density changes with zoom):

```bash
dotnet run -- zoom-sample --x 0.5 --y 0.5 --zoom 2.0 --seed 1337
```

Authoring file format:

```json
[
  {
    "x": 0,
    "y": 0,
    "seed": 1337,
    "features": {
      "biome": "mechanical garden",
      "mood": "curious",
      "motifWeights": { "pistons": 0.6, "roots": 0.4 },
      "materialWeights": { "brass": 0.7, "steam": 0.3 },
      "paletteWeights": { "amber": 0.8, "ivory": 0.2 }
    }
  }
]
```

Generated image and metadata files are written to `Tilez/generated/`.
