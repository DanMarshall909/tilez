using System.Globalization;
using System.Text;

namespace Tilez;

public sealed class SingleTileGenerator
{
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
        var imagePath = GenerateImage(coordinate, seed, paletteWeights, outputRoot);

        return new WorldTile(coordinate, seed, features, tileSummary, imagePath);
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

        var normalized = values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value / sum);

        return normalized;
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

    private static string GenerateImage(
        TileCoordinate coordinate,
        int seed,
        IReadOnlyDictionary<string, float> paletteWeights,
        string outputRoot)
    {
        var imagesDir = Path.Combine(outputRoot, "images");
        Directory.CreateDirectory(imagesDir);

        var fileName = $"tile_{coordinate.X}_{coordinate.Y}_{seed}.ppm";
        var filePath = Path.Combine(imagesDir, fileName);

        var color = PaletteToRgb(paletteWeights);
        var ppm = BuildPpm(64, 64, color);
        File.WriteAllText(filePath, ppm, Encoding.ASCII);

        return filePath;
    }

    private static (int R, int G, int B) PaletteToRgb(IReadOnlyDictionary<string, float> paletteWeights)
    {
        var amber = paletteWeights.GetValueOrDefault("amber");
        var verdigris = paletteWeights.GetValueOrDefault("verdigris");
        var violet = paletteWeights.GetValueOrDefault("violet");
        var teal = paletteWeights.GetValueOrDefault("teal");
        var rust = paletteWeights.GetValueOrDefault("rust");
        var ivory = paletteWeights.GetValueOrDefault("ivory");

        var r = Clamp01(amber * 0.9f + rust * 0.8f + ivory * 0.7f + violet * 0.3f);
        var g = Clamp01(verdigris * 0.9f + teal * 0.7f + ivory * 0.6f);
        var b = Clamp01(violet * 0.9f + teal * 0.6f + ivory * 0.5f);

        return ((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    private static string BuildPpm(int width, int height, (int R, int G, int B) baseColor)
    {
        var sb = new StringBuilder();
        sb.AppendLine("P3");
        sb.AppendLine($"{width} {height}");
        sb.AppendLine("255");

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var wave = (float)(Math.Sin((x + y) * 0.2) * 0.15 + 0.85);
                var r = (int)(baseColor.R * wave);
                var g = (int)(baseColor.G * wave);
                var b = (int)(baseColor.B * wave);

                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(g.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(b.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
