namespace Tilez;

public sealed class CameraPanEngine
{
    private readonly SingleTileGenerator _generator;
    private readonly HashSet<string> _generated = [];

    public CameraPanEngine(SingleTileGenerator generator)
    {
        _generator = generator;
    }

    public int PanAndGenerate(
        float startX,
        float startY,
        float endX,
        float endY,
        int viewportWidth,
        int viewportHeight,
        int steps,
        int seed,
        string outputRoot)
    {
        var generatedCount = 0;
        steps = Math.Max(steps, 1);

        for (var step = 0; step <= steps; step++)
        {
            var t = step / (float)steps;
            var centerX = Lerp(startX, endX, t);
            var centerY = Lerp(startY, endY, t);

            var candidates = GetVisibleCandidates(centerX, centerY, viewportWidth, viewportHeight)
                .OrderBy(pair => pair.Priority)
                .ToList();

            foreach (var candidate in candidates)
            {
                var key = Key(candidate.X, candidate.Y, seed);
                if (_generated.Contains(key))
                {
                    continue;
                }

                var tile = _generator.Generate(new TileCoordinate(candidate.X, candidate.Y), seed, outputRoot);
                TilePersistence.SaveMetadata(tile, outputRoot);
                _generated.Add(key);
                generatedCount++;
            }
        }

        return generatedCount;
    }

    private static IEnumerable<(int X, int Y, float Priority)> GetVisibleCandidates(
        float centerX,
        float centerY,
        int viewportWidth,
        int viewportHeight)
    {
        var halfW = viewportWidth / 2f;
        var halfH = viewportHeight / 2f;

        var minX = (int)MathF.Floor(centerX - halfW);
        var maxX = (int)MathF.Floor(centerX + halfW);
        var minY = (int)MathF.Floor(centerY - halfH);
        var maxY = (int)MathF.Floor(centerY + halfH);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var dx = (x + 0.5f) - centerX;
                var dy = (y + 0.5f) - centerY;
                var priority = MathF.Sqrt(dx * dx + dy * dy);
                yield return (x, y, priority);
            }
        }
    }

    private static float Lerp(float start, float end, float t)
    {
        return start + ((end - start) * t);
    }

    private static string Key(int x, int y, int seed)
    {
        return $"{x}:{y}:{seed}";
    }
}
