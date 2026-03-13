using Tilez;

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
