namespace Tilez;

public sealed class SingleTileGenerator
{
    private readonly ITileImageGenerator _imageGenerator;

    public SingleTileGenerator(ITileImageGenerator? imageGenerator = null)
    {
        _imageGenerator = imageGenerator ?? new ProceduralTileImageGenerator();
    }

    private static readonly string[] Biomes =
    [
        "mechanical garden",
        "fungal bloom",
        "sunken archive",
        "glass desert",
        "storm marsh"
    ];

    private static readonly string[] Moods =
    [
        "curious",
        "tense",
        "tranquil",
        "ominous",
        "hopeful"
    ];

    private static readonly string[] Motifs =
    [
        "pistons",
        "roots",
        "spores",
        "runes",
        "clockwork",
        "vines"
    ];

    private static readonly string[] Materials =
    [
        "brass",
        "stone",
        "mycelium",
        "glass",
        "steam",
        "copper"
    ];

    private static readonly string[] Palettes =
    [
        "amber",
        "verdigris",
        "violet",
        "teal",
        "rust",
        "ivory"
    ];

    public WorldTile Generate(TileCoordinate coordinate, int seed, string outputRoot)
    {
        var random = new Random(CombineSeed(seed, coordinate));

        var biome = Biomes[random.Next(Biomes.Length)];
        var mood = Moods[random.Next(Moods.Length)];

        var motifWeights = BuildWeights(Motifs, random);
        var materialWeights = BuildWeights(Materials, random);
        var paletteWeights = BuildWeights(Palettes, random);

        var features = new PromptFeatures(
            biome,
            mood,
            motifWeights,
            materialWeights,
            paletteWeights);

        var tileSummary = BuildSummary(features);
        var imagePath = GenerateImage(coordinate, seed, features, outputRoot, 64, false, "base");

        return new WorldTile(coordinate, seed, features, tileSummary, imagePath);
    }

    public WorldTile GenerateFromFeatures(
        TileCoordinate coordinate,
        int seed,
        PromptFeatures features,
        string outputRoot)
    {
        var tileSummary = BuildSummary(features);
        var imagePath = GenerateImage(coordinate, seed, features, outputRoot, 64, false, "base");
        return new WorldTile(coordinate, seed, features, tileSummary, imagePath);
    }

    public string RenderVariant(WorldTile tile, string outputRoot, int size, bool highDetail, string tag)
    {
        return GenerateImage(
            tile.Coordinate,
            tile.Seed,
            tile.Features,
            outputRoot,
            size,
            highDetail,
            tag);
    }

    private string GenerateImage(
        TileCoordinate coordinate,
        int seed,
        PromptFeatures features,
        string outputRoot,
        int size,
        bool highDetail,
        string tag)
    {
        var imagesDir = Path.Combine(outputRoot, "images");
        Directory.CreateDirectory(imagesDir);

        var fileName = $"tile_{coordinate.X}_{coordinate.Y}_{seed}_{tag}_{size}.png";
        var filePath = Path.Combine(imagesDir, fileName);

        var request = new TileImageRequest(
            coordinate,
            seed,
            features,
            size,
            highDetail,
            tag,
            filePath);

        _imageGenerator.Generate(request);
        return filePath;
    }

    private static int CombineSeed(int seed, TileCoordinate coordinate)
    {
        unchecked
        {
            var hash = seed;
            hash = (hash * 397) ^ coordinate.X;
            hash = (hash * 397) ^ coordinate.Y;
            return hash;
        }
    }

    private static IReadOnlyDictionary<string, float> BuildWeights(IEnumerable<string> keys, Random random)
    {
        var values = keys.ToDictionary(k => k, _ => (float)random.NextDouble());
        var sum = values.Values.Sum();

        if (sum <= 0f)
        {
            return values;
        }

        return values.ToDictionary(pair => pair.Key, pair => pair.Value / sum);
    }

    private static string BuildSummary(PromptFeatures features)
    {
        var motif = TopKey(features.MotifWeights);
        var material = TopKey(features.MaterialWeights);
        return $"{features.Biome}, {features.Mood} tone with {material} and {motif}";
    }

    private static string TopKey(IReadOnlyDictionary<string, float> values)
    {
        return values.OrderByDescending(pair => pair.Value).First().Key;
    }
}
