using System.Numerics;

namespace Tilez;

public sealed record BlendedPromptFeatures(
    string DominantBiome,
    float Mechanicality,
    float Organicity,
    float Harmony,
    float Conflict,
    IReadOnlyDictionary<string, float> MotifWeights,
    IReadOnlyDictionary<string, float> MaterialWeights,
    IReadOnlyDictionary<string, float> PaletteWeights);

public interface IPromptFieldSampler
{
    BlendedPromptFeatures SampleAt(
        Vector2 worldPosition,
        IReadOnlyList<WorldTile> nearbyTiles);
}

public sealed class DistanceWeightedPromptFieldSampler : IPromptFieldSampler
{
    private readonly float _sigma;

    public DistanceWeightedPromptFieldSampler(float sigma = 0.9f)
    {
        _sigma = sigma;
    }

    public BlendedPromptFeatures SampleAt(Vector2 worldPosition, IReadOnlyList<WorldTile> nearbyTiles)
    {
        if (nearbyTiles.Count == 0)
        {
            throw new ArgumentException("At least one nearby tile is required.");
        }

        var tileWeights = new List<(WorldTile Tile, float Weight)>(nearbyTiles.Count);
        var totalWeight = 0f;

        foreach (var tile in nearbyTiles)
        {
            var center = new Vector2(tile.Coordinate.X + 0.5f, tile.Coordinate.Y + 0.5f);
            var distance = Vector2.Distance(worldPosition, center);
            var weight = Gaussian(distance, _sigma);
            tileWeights.Add((tile, weight));
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            totalWeight = 1f;
        }

        var biomeScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var motifWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var materialWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var paletteWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var mechanicality = 0f;
        var organicity = 0f;
        var harmony = 0f;
        var conflict = 0f;

        foreach (var (tile, weight) in tileWeights)
        {
            var normalized = weight / totalWeight;

            AddScore(biomeScores, tile.Features.Biome, normalized);
            MergeWeights(motifWeights, tile.Features.MotifWeights, normalized);
            MergeWeights(materialWeights, tile.Features.MaterialWeights, normalized);
            MergeWeights(paletteWeights, tile.Features.PaletteWeights, normalized);

            mechanicality += normalized * ScoreMechanicality(tile.Features);
            organicity += normalized * ScoreOrganicity(tile.Features);
            harmony += normalized * ScoreHarmony(tile.Features);
            conflict += normalized * ScoreConflict(tile.Features);
        }

        var dominantBiome = biomeScores.OrderByDescending(pair => pair.Value).First().Key;

        return new BlendedPromptFeatures(
            dominantBiome,
            mechanicality,
            organicity,
            harmony,
            conflict,
            Normalize(motifWeights),
            Normalize(materialWeights),
            Normalize(paletteWeights));
    }

    private static float Gaussian(float distance, float sigma)
    {
        if (sigma <= 0f)
        {
            return 1f;
        }

        var sigma2 = sigma * sigma;
        var exponent = -(distance * distance) / (2f * sigma2);
        return (float)Math.Exp(exponent);
    }

    private static void AddScore(IDictionary<string, float> map, string key, float value)
    {
        map[key] = ReadOrZero(map, key) + value;
    }

    private static void MergeWeights(
        IDictionary<string, float> destination,
        IReadOnlyDictionary<string, float> source,
        float scalar)
    {
        foreach (var pair in source)
        {
            destination[pair.Key] = ReadOrZero(destination, pair.Key) + pair.Value * scalar;
        }
    }

    private static float ReadOrZero(IDictionary<string, float> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : 0f;
    }

    private static IReadOnlyDictionary<string, float> Normalize(IDictionary<string, float> values)
    {
        var sum = values.Values.Sum();
        if (sum <= 0f)
        {
            return new Dictionary<string, float>(values);
        }

        return values.ToDictionary(pair => pair.Key, pair => pair.Value / sum);
    }

    private static float ScoreMechanicality(PromptFeatures features)
    {
        return features.MaterialWeights.GetValueOrDefault("brass")
            + features.MaterialWeights.GetValueOrDefault("copper")
            + features.MotifWeights.GetValueOrDefault("pistons")
            + features.MotifWeights.GetValueOrDefault("clockwork");
    }

    private static float ScoreOrganicity(PromptFeatures features)
    {
        return features.MaterialWeights.GetValueOrDefault("mycelium")
            + features.MotifWeights.GetValueOrDefault("roots")
            + features.MotifWeights.GetValueOrDefault("vines")
            + features.MotifWeights.GetValueOrDefault("spores");
    }

    private static float ScoreHarmony(PromptFeatures features)
    {
        return features.PaletteWeights.GetValueOrDefault("ivory")
            + features.PaletteWeights.GetValueOrDefault("teal");
    }

    private static float ScoreConflict(PromptFeatures features)
    {
        return features.PaletteWeights.GetValueOrDefault("rust")
            + features.PaletteWeights.GetValueOrDefault("violet");
    }
}

public static class PromptComposer
{
    public static string Compose(BlendedPromptFeatures blended)
    {
        var motif = TopKey(blended.MotifWeights);
        var material = TopKey(blended.MaterialWeights);
        var palette = TopKey(blended.PaletteWeights);
        return $"{blended.DominantBiome} with {material} {motif}, {palette} palette";
    }

    private static string TopKey(IReadOnlyDictionary<string, float> values)
    {
        return values.Count == 0
            ? "neutral"
            : values.OrderByDescending(pair => pair.Value).First().Key;
    }
}
