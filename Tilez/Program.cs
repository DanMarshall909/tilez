using Tilez;
using System.Numerics;

var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "generated");
Directory.CreateDirectory(outputRoot);

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "single";
var generator = new SingleTileGenerator();
var painter = new WorldPainter(generator);

switch (mode)
{
    case "paint":
        {
            var minX = GetInt(args, "--min-x", 0);
            var maxX = GetInt(args, "--max-x", 2);
            var minY = GetInt(args, "--min-y", 0);
            var maxY = GetInt(args, "--max-y", 2);
            var seed = GetInt(args, "--seed", 1337);

            var tiles = painter.PaintRectangle(minX, maxX, minY, maxY, seed, outputRoot);
            Console.WriteLine($"Painted {tiles.Count} tiles.");
            break;
        }

    case "author":
        {
            var filePath = GetString(args, "--file");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Missing required --file path for author mode.");
            }

            var tiles = painter.PaintFromAuthoringFile(filePath, outputRoot);
            Console.WriteLine($"Authored {tiles.Count} tiles from {filePath}.");
            break;
        }

    case "prefetch":
        {
            var viewportMinX = GetInt(args, "--viewport-min-x", 0);
            var viewportMaxX = GetInt(args, "--viewport-max-x", 2);
            var viewportMinY = GetInt(args, "--viewport-min-y", 0);
            var viewportMaxY = GetInt(args, "--viewport-max-y", 2);
            var margin = GetInt(args, "--margin", 1);
            var seed = GetInt(args, "--seed", 1337);

            var tiles = painter.PaintViewportWithMargin(
                viewportMinX,
                viewportMaxX,
                viewportMinY,
                viewportMaxY,
                margin,
                seed,
                outputRoot);

            Console.WriteLine(
                $"Prefetched {tiles.Count} tiles for viewport " +
                $"[{viewportMinX},{viewportMinY}] to [{viewportMaxX},{viewportMaxY}] with margin {margin}.");
            break;
        }

    case "sample":
        {
            var sampleX = GetFloat(args, "--x", 0.5f);
            var sampleY = GetFloat(args, "--y", 0.5f);
            var seed = GetInt(args, "--seed", 1337);

            var nearbyTiles = painter.PaintRectangle(-1, 1, -1, 1, seed, outputRoot);
            var sampler = new DistanceWeightedPromptFieldSampler();
            var blended = sampler.SampleAt(new Vector2(sampleX, sampleY), nearbyTiles);
            var prompt = PromptComposer.Compose(blended);

            Console.WriteLine($"Sample position: ({sampleX:0.00}, {sampleY:0.00})");
            Console.WriteLine($"Dominant biome: {blended.DominantBiome}");
            Console.WriteLine($"Prompt: {prompt}");
            break;
        }

    default:
        {
            var x = GetInt(args, "--x", 0);
            var y = GetInt(args, "--y", 0);
            var seed = GetInt(args, "--seed", 1337);

            var tile = generator.Generate(new TileCoordinate(x, y), seed, outputRoot);
            var metadataPath = TilePersistence.SaveMetadata(tile, outputRoot);

            Console.WriteLine($"Generated tile: {tile.Coordinate.X},{tile.Coordinate.Y}");
            Console.WriteLine($"Summary: {tile.TileSummary}");
            Console.WriteLine($"Image: {tile.ImagePath}");
            Console.WriteLine($"Metadata: {metadataPath}");
            break;
        }
}

static int GetInt(string[] args, string key, int defaultValue)
{
    var text = GetString(args, key);
    return int.TryParse(text, out var parsed) ? parsed : defaultValue;
}

static float GetFloat(string[] args, string key, float defaultValue)
{
    var text = GetString(args, key);
    return float.TryParse(text, out var parsed) ? parsed : defaultValue;
}

static string? GetString(string[] args, string key)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}
