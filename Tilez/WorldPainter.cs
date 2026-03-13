using System.Text.Json;

namespace Tilez;

public sealed class WorldPainter
{
    private readonly SingleTileGenerator _generator;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WorldPainter(SingleTileGenerator generator)
    {
        _generator = generator;
    }

    public IReadOnlyList<WorldTile> PaintRectangle(
        int minX,
        int maxX,
        int minY,
        int maxY,
        int seed,
        string outputRoot)
    {
        if (minX > maxX || minY > maxY)
        {
            throw new ArgumentException("Invalid paint bounds.");
        }

        var tiles = new List<WorldTile>();

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var tile = _generator.Generate(new TileCoordinate(x, y), seed, outputRoot);
                TilePersistence.SaveMetadata(tile, outputRoot);
                tiles.Add(tile);
            }
        }

        return tiles;
    }

    public IReadOnlyList<WorldTile> PaintFromAuthoringFile(string authoringFilePath, string outputRoot)
    {
        var text = File.ReadAllText(authoringFilePath);
        var requests = JsonSerializer.Deserialize<List<TileAuthoringRequest>>(text, JsonOptions)
            ?? throw new InvalidOperationException("Authoring file is empty or invalid.");

        var tiles = new List<WorldTile>(requests.Count);

        foreach (var request in requests)
        {
            var coordinate = new TileCoordinate(request.X, request.Y);
            var tile = request.Features is null
                ? _generator.Generate(coordinate, request.Seed, outputRoot)
                : _generator.GenerateFromFeatures(coordinate, request.Seed, request.Features, outputRoot);

            TilePersistence.SaveMetadata(tile, outputRoot);
            tiles.Add(tile);
        }

        return tiles;
    }
}
