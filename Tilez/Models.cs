namespace Tilez;

public sealed record TileCoordinate(int X, int Y);

public sealed record PromptFeatures(
    string Biome,
    string Mood,
    IReadOnlyDictionary<string, float> MotifWeights,
    IReadOnlyDictionary<string, float> MaterialWeights,
    IReadOnlyDictionary<string, float> PaletteWeights);

public sealed record WorldTile(
    TileCoordinate Coordinate,
    int Seed,
    PromptFeatures Features,
    string TileSummary,
    string ImagePath);
