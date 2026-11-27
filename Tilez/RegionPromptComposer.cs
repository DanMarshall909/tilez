using System.Text;

namespace Tilez;

public static class RegionPromptComposer
{
    public static string Compose(int originX, int originY, int columns, int rows, int seed,
        string outputRoot, bool outpainting)
    {
        var center = TilePersistence.GetOrCreateMetadata(
            new TileCoordinate(originX + columns / 2, originY + rows / 2), seed, outputRoot).Features;
        var prompt = new StringBuilder(outpainting
            ? "Outpaint only the pale blank area of this single continuous overhead fantasy landscape. Keep existing artwork fixed and extend its roads, rivers, ground cover, perspective and lighting into the new area."
            : "Create one continuous overhead fantasy landscape with connected ground, winding roads, rivers and structures embedded in the terrain.");
        prompt.Append(" The shared landscape has a ").Append(center.Biome)
            .Append(" character and ").Append(center.Mood)
            .Append(" atmosphere, with ").Append(TopTwo(center.MotifWeights))
            .Append(" woven into ").Append(TopTwo(center.MaterialWeights))
            .Append(" and ").Append(TopOne(center.PaletteWeights))
            .Append(" colours. Its terrain shifts gradually across the same open ground.");

        for (var row = 0; row < rows; row++)
        {
            prompt.Append(" Across the ").Append(RowName(row, rows)).Append(" terrain, the scene moves from ");
            for (var column = 0; column < columns; column++)
            {
                if (column > 0) prompt.Append(column == columns - 1 ? " toward " : " through ");
                var coordinate = new TileCoordinate(originX + column, originY + row);
                var features = TilePersistence.GetOrCreateMetadata(coordinate, seed, outputRoot).Features;
                prompt.Append(features.Biome).Append(" terrain");
            }
            prompt.Append(", with the same paths and ground cover flowing between them.");
        }

        prompt.Append(" These are gradual changes within one traversable place. Paths, terrain and vegetation flow through every location without enclosed patches. Artwork fills the canvas and continues beyond every edge. No circular vignettes, medallions, icons, specimen displays, isolated scenes, frame, margins, paper, grid lines, tile boundaries, labels or panels.");
        return prompt.ToString();
    }

    private static string RowName(int row, int rows) => rows == 3
        ? new[] { "northern", "central", "southern" }[row]
        : new[] { "far northern", "northern", "southern", "far southern" }[row];

    private static string TopTwo(IReadOnlyDictionary<string, float> weights)
        => weights.Count == 0
            ? "neutral"
            : string.Join(" and ", weights.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(2).Select(pair => pair.Key));

    private static string TopOne(IReadOnlyDictionary<string, float> weights)
        => weights.Count == 0
            ? "natural"
            : weights.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
}
