using System.Text.Json;

namespace Tilez;

public static class TilePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SaveMetadata(WorldTile tile, string outputRoot)
    {
        var filePath = MetadataPath(tile.Coordinate, tile.Seed, outputRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.Serialize(tile, JsonOptions);

        File.WriteAllText(filePath, json);
        return filePath;
    }

    public static WorldTile GetOrCreateMetadata(TileCoordinate coordinate, int seed, string outputRoot)
    {
        var path = MetadataPath(coordinate, seed, outputRoot);
        if (File.Exists(path)) return LoadMetadata(path, coordinate, seed);

        var features = SingleTileGenerator.CreateFeatures(coordinate, seed);
        var tile = new WorldTile(coordinate, seed, features, "", "");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(tile, JsonOptions));
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return LoadMetadata(path, coordinate, seed);
    }

    public static string MetadataPath(TileCoordinate coordinate, int seed, string outputRoot)
        => Path.Combine(outputRoot, "metadata", $"tile_{coordinate.X}_{coordinate.Y}_{seed}.json");

    private static WorldTile LoadMetadata(string path, TileCoordinate coordinate, int seed)
    {
        var tile = JsonSerializer.Deserialize<WorldTile>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Tile metadata is empty: {path}");
        if (tile.Coordinate != coordinate || tile.Seed != seed || tile.Features is null)
            throw new InvalidDataException($"Tile metadata identity does not match its path: {path}");
        return tile;
    }
}
