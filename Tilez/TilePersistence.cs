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
        var metadataDir = Path.Combine(outputRoot, "metadata");
        Directory.CreateDirectory(metadataDir);

        var fileName = $"tile_{tile.Coordinate.X}_{tile.Coordinate.Y}_{tile.Seed}.json";
        var filePath = Path.Combine(metadataDir, fileName);
        var json = JsonSerializer.Serialize(tile, JsonOptions);

        File.WriteAllText(filePath, json);
        return filePath;
    }
}
