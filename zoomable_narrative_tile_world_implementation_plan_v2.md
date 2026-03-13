
# Zoomable Narrative Tile World — Implementation Plan (Revised)

This version updates the specification to emphasise **prompt‑field influence instead of pixel blending**, and introduces **GPU‑assisted resolution scaling** as an optimisation path.

The system should evolve incrementally while preserving these two architectural principles.

---

# Key Architectural Principle

## The world is a prompt influence field

Tiles are **not isolated images**.  
Each tile acts as a **semantic influence source** across space.

Rendering must derive from **blended prompt influence**, not from blending pixels between tile images.

Pipeline:

Tile prompts → spatial influence field → derived prompt → image generation

This ensures:

• coherent transitions  
• meaningful interactions between tiles  
• narrative continuity  
• zoom behaviour that reveals structure instead of blur  

Do **not** implement tile seams using pixel interpolation.

Pixel blending produces visual artefacts and destroys semantic continuity.

---

# Prompt Influence Model

Each tile has a centre point and a prompt definition.

The prompt strength decays as distance from the centre increases.

For a point P:

distance_i = distance(P, tile_center_i)

weight_i = falloff(distance_i)

Typical falloff functions:

Gaussian:
w = exp(-(d² / 2σ²))

Inverse distance:
w = 1 / (d + ε)

Normalised influence:

normalized_w_i = w_i / Σ(w_all)

Only nearby tiles need to be considered (normally a 3×3 tile window).

---

# What Is Blended

Do **not blend raw prompt text**.

Instead blend **structured prompt features** such as:

• biome type  
• mood  
• motif weights  
• material weights  
• narrative forces  
• palette hints  

Example:

Tile A:
mechanical garden — brass, pistons, steam

Tile B:
fungal bloom — spores, roots, glow

Near the seam the derived prompt may become:

biomechanical fungal machinery — brass roots, steam spores

This derived prompt is used when generating imagery for that location.

---

# Seam Behaviour

Tile edges are interaction zones.

When influence from multiple tiles is similar, seam areas should amplify interaction.

Possible seam behaviours:

• fusion  
• invasion  
• trade  
• tension  
• reflection  
• transformation  

Edge proximity may increase interaction strength.

---

# Zoom Behaviour

Zoom does **not simply enlarge images**.

Zoom changes **sampling density of the prompt field**.

Far zoom:
• coarse sampling
• regional patterns

Close zoom:
• dense sampling
• detailed interactions

Tiles may expose additional detail levels when zoom thresholds are crossed.

---

# GPU Assisted Resolution Scaling

Image rendering can be expensive. The system may use **GPU scaling technologies** to improve performance.

Examples include:

• DLSS (NVIDIA)
• FSR (AMD)
• XeSS (Intel)
• DirectML super‑resolution
• Tensor upscaling models

Strategy:

1. Generate tiles at a moderate base resolution.
2. Use GPU scaling to upscale when displayed.
3. Generate true high‑detail images only when zoom level demands it.

Benefits:

• lower generation cost
• faster viewport updates
• smoother panning
• reduced storage footprint

Rendering stages may be:

Stage 1 — metadata only  
Stage 2 — low resolution tile render  
Stage 3 — GPU upscale for display  
Stage 4 — full resolution refinement when zoomed deeply

GPU scaling should be treated as a **display optimisation**, not as a substitute for true detail generation.

---

# Incremental Implementation Phases

The development order remains:

1. Generate a single tile
2. Paint tiles
3. Offscreen generation margin
4. Location‑sensitive prompting
5. Camera movement
6. Zooming
7. Prompt‑field blending
8. GPU assisted scaling optimisation

Each phase must produce a working system before continuing.

---

# Tile Data Model

Example base tile structure:

```csharp
public sealed record WorldTile(
    TileCoordinate Coordinate,
    int Seed,
    PromptFeatures Features,
    string TileSummary,
    string ImagePath);
```

Prompt feature model:

```csharp
public sealed record PromptFeatures(
    string Biome,
    string Mood,
    IReadOnlyDictionary<string,float> MotifWeights,
    IReadOnlyDictionary<string,float> MaterialWeights,
    IReadOnlyDictionary<string,float> PaletteWeights);
```

---

# Prompt Field Sampling Interface

```csharp
public interface IPromptFieldSampler
{
    BlendedPromptFeatures SampleAt(
        Vector2 worldPosition,
        IReadOnlyList<WorldTile> nearbyTiles);
}
```

Example result:

```csharp
public sealed record BlendedPromptFeatures(
    string DominantBiome,
    float Mechanicality,
    float Organicity,
    float Harmony,
    float Conflict,
    IReadOnlyDictionary<string,float> MotifWeights);
```

---

# Rendering Pipeline

Tile prompts generate metadata first.

Rendering may proceed as:

1. Tile prompt generation
2. Metadata persistence
3. Low‑resolution render
4. GPU upscaled display
5. High detail refinement (on demand)

Rendering must remain **lazy and prioritised** based on viewport visibility.

---

# Performance Guidance

High value optimisations:

• deterministic seed generation  
• chunked world storage  
• in‑memory + disk tile caching  
• metadata‑first generation  
• priority queue for visible tiles  
• cancellation of obsolete generation jobs  
• GPU upscaling for display resolution  
• structured prompt storage instead of long text prompts  

---

# Important Restrictions

Until the base phases are stable do **not** implement:

• full recursive subtile hierarchies  
• large narrative graph systems  
• distributed generation workers  
• pixel seam blending  

Focus on the prompt‑field architecture first.

---

# Final Instruction

The system must treat tiles as **semantic influence sources in a spatial field**, not static images.

All rendering decisions should ultimately derive from **prompt influence blending**, ensuring that neighbouring tiles interact meaningfully across the world.

