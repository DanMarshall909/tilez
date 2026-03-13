using System.Numerics;

namespace Tilez;

public sealed class ZoomPromptSamplingService
{
    private readonly WorldPainter _worldPainter;
    private readonly IPromptFieldSampler _sampler;

    public ZoomPromptSamplingService(WorldPainter worldPainter, IPromptFieldSampler sampler)
    {
        _worldPainter = worldPainter;
        _sampler = sampler;
    }

    public BlendedPromptFeatures SampleAtZoom(Vector2 worldPosition, float zoomLevel, int seed, string outputRoot)
    {
        var density = DetermineDensity(zoomLevel);
        var radius = DetermineRadius(zoomLevel);

        var cx = (int)MathF.Floor(worldPosition.X);
        var cy = (int)MathF.Floor(worldPosition.Y);
        var nearbyTiles = _worldPainter.PaintRectangle(cx - radius, cx + radius, cy - radius, cy + radius, seed, outputRoot);

        var samples = CreateSamplePoints(worldPosition, density)
            .Select(point => _sampler.SampleAt(point, nearbyTiles))
            .ToList();

        return Average(samples);
    }

    private static int DetermineDensity(float zoomLevel)
    {
        if (zoomLevel < 1.0f)
        {
            return 1;
        }

        if (zoomLevel < 2.0f)
        {
            return 4;
        }

        return 9;
    }

    private static int DetermineRadius(float zoomLevel)
    {
        return zoomLevel < 1.0f ? 1 : 2;
    }

    private static IEnumerable<Vector2> CreateSamplePoints(Vector2 center, int density)
    {
        if (density == 1)
        {
            yield return center;
            yield break;
        }

        var side = (int)MathF.Sqrt(density);
        var step = 0.2f;
        var half = (side - 1) / 2f;

        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var offsetX = (x - half) * step;
                var offsetY = (y - half) * step;
                yield return new Vector2(center.X + offsetX, center.Y + offsetY);
            }
        }
    }

    private static BlendedPromptFeatures Average(IReadOnlyList<BlendedPromptFeatures> samples)
    {
        var count = Math.Max(samples.Count, 1);
        var biomeScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var motif = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var palettes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var mechanicality = 0f;
        var organicity = 0f;
        var harmony = 0f;
        var conflict = 0f;

        foreach (var sample in samples)
        {
            biomeScores[sample.DominantBiome] = ReadOrZero(biomeScores, sample.DominantBiome) + 1f;
            Merge(motif, sample.MotifWeights);
            Merge(materials, sample.MaterialWeights);
            Merge(palettes, sample.PaletteWeights);
            mechanicality += sample.Mechanicality;
            organicity += sample.Organicity;
            harmony += sample.Harmony;
            conflict += sample.Conflict;
        }

        var dominantBiome = biomeScores.Count == 0
            ? "neutral"
            : biomeScores.OrderByDescending(pair => pair.Value).First().Key;

        return new BlendedPromptFeatures(
            dominantBiome,
            mechanicality / count,
            organicity / count,
            harmony / count,
            conflict / count,
            Normalize(motif),
            Normalize(materials),
            Normalize(palettes));
    }

    private static void Merge(IDictionary<string, float> destination, IReadOnlyDictionary<string, float> source)
    {
        foreach (var pair in source)
        {
            destination[pair.Key] = ReadOrZero(destination, pair.Key) + pair.Value;
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
}
