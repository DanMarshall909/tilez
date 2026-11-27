using SkiaSharp;

namespace Tilez;

public sealed record RegionImageRequest(string Prompt, int Seed, string AspectRatio, byte[]? ReferencePng);

public interface IRegionImageGenerator
{
    byte[] Generate(RegionImageRequest request);
}

public sealed class RegionRenderer(IRegionImageGenerator backend)
{
    private const int RegionSize = 3;
    private const int ReferenceCellSize = 256;

    public static string TilePath(string root, int x, int y, int seed, int size)
    {
        var fileName = size switch
        {
            64 => $"tile_{x}_{y}_{seed}_base_64.png",
            128 => $"tile_{x}_{y}_{seed}_base_64_upscaled_128.png",
            256 => $"tile_{x}_{y}_{seed}_high_256.png",
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };
        return Path.Combine(root, "images", fileName);
    }

    public static string MarkerPath(string root, int regionX, int regionY, int seed)
        => Path.Combine(root, "regions", $"region_{regionX}_{regionY}_{seed}.complete");

    public void Render(int regionX, int regionY, int seed, string outputRoot)
    {
        var originX = checked(regionX * RegionSize);
        var originY = checked(regionY * RegionSize);
        var marker = MarkerPath(outputRoot, regionX, regionY, seed);
        var missing = new List<(int X, int Y)>();
        for (var x = originX; x < originX + RegionSize; x++)
        for (var y = originY; y < originY + RegionSize; y++)
        {
            if (!IsAccepted(TilePath(outputRoot, x, y, seed, 128))) missing.Add((x, y));
        }

        if (missing.Count > 0)
        {
            var layout = SelectLayout(originX, originY, seed, outputRoot);
            var reference = CreateReference(layout, seed, outputRoot);
            var prompt = reference is null
                ? "One continuous top-down illustrated fantasy world map, coherent terrain, roads and structures across the entire image. No borders, grid lines, labels or separate panels."
                : "Outpaint the pale blank area of this top-down illustrated world map. Extend the existing roads, structures, terrain, palette and lighting naturally into the blank area. Keep the existing artwork in its exact position. One continuous scene, no borders, grid lines, labels or separate panels.";
            var bytes = backend.Generate(new RegionImageRequest(prompt, RegionSeed(seed, regionX, regionY), layout.AspectRatio, reference));
            using var generated = PngImages.Decode(bytes);
            if (generated.Width < layout.Columns * 64 || generated.Height < layout.Rows * 64)
                throw new InvalidDataException("Region image is too small to split into tiles.");
            var expectedRatio = (double)layout.Columns / layout.Rows;
            var actualRatio = (double)generated.Width / generated.Height;
            if (Math.Abs(actualRatio - expectedRatio) > 0.06)
                throw new InvalidDataException("Region image has the wrong aspect ratio.");

            Directory.CreateDirectory(Path.Combine(outputRoot, "images"));
            foreach (var (x, y) in missing)
            {
                var column = x - layout.OriginX;
                var row = y - layout.OriginY;
                using var tile = new SKBitmap(256, 256);
                using (var canvas = new SKCanvas(tile))
                {
                    var source = new SKRect(
                        generated.Width * column / (float)layout.Columns,
                        generated.Height * row / (float)layout.Rows,
                        generated.Width * (column + 1) / (float)layout.Columns,
                        generated.Height * (row + 1) / (float)layout.Rows);
                    canvas.DrawBitmap(generated, source, new SKRect(0, 0, 256, 256),
                        new SKSamplingOptions(SKFilterMode.Linear));
                }
                ConstrainAcceptedEdges(tile, x, y, seed, outputRoot, missing);
                using var low = PngImages.Resize(tile, 128, SKFilterMode.Linear);
                using var tiny = PngImages.Resize(tile, 64, SKFilterMode.Linear);
                SaveNew(tiny, TilePath(outputRoot, x, y, seed, 64));
                SaveNew(low, TilePath(outputRoot, x, y, seed, 128));
                SaveNew(tile, TilePath(outputRoot, x, y, seed, 256));
            }
        }

        var hadMarker = File.Exists(marker);
        if (!hadMarker || Enumerable.Range(originX, RegionSize).Any(x =>
                Enumerable.Range(originY, RegionSize).Any(y => !IsAccepted(TilePath(outputRoot, x, y, seed, 256)))))
        {
            for (var x = originX; x < originX + RegionSize; x++)
            for (var y = originY; y < originY + RegionSize; y++)
            {
                var high = TilePath(outputRoot, x, y, seed, 256);
                if (IsAccepted(high) && (hadMarker || missing.Contains((x, y)))) continue;
                using var accepted = PngImages.Decode(TilePath(outputRoot, x, y, seed, 128));
                using var resized = PngImages.Resize(accepted, 256, SKFilterMode.Linear);
                PngImages.Save(resized, high);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "region-v1");
        }
    }

    private static Layout SelectLayout(int x, int y, int seed, string root)
    {
        for (var dx = 0; dx < RegionSize; dx++)
        for (var dy = 0; dy < RegionSize; dy++)
        {
            if (IsAccepted(TilePath(root, x + dx, y + dy, seed, 128))) return new(x, y, 3, 3);
        }

        if (Enumerable.Range(0, 3).Any(dy => IsAccepted(TilePath(root, x - 1, y + dy, seed, 128))))
            return new(x - 1, y, 4, 3);
        if (Enumerable.Range(0, 3).Any(dy => IsAccepted(TilePath(root, x + 3, y + dy, seed, 128))))
            return new(x, y, 4, 3);
        if (Enumerable.Range(0, 3).Any(dx => IsAccepted(TilePath(root, x + dx, y - 1, seed, 128))))
            return new(x, y - 1, 3, 4);
        if (Enumerable.Range(0, 3).Any(dx => IsAccepted(TilePath(root, x + dx, y + 3, seed, 128))))
            return new(x, y, 3, 4);
        if (IsAccepted(TilePath(root, x - 1, y - 1, seed, 128)))
            return new(x - 1, y - 1, 4, 4);
        if (IsAccepted(TilePath(root, x + 3, y - 1, seed, 128)))
            return new(x, y - 1, 4, 4);
        if (IsAccepted(TilePath(root, x - 1, y + 3, seed, 128)))
            return new(x - 1, y, 4, 4);
        if (IsAccepted(TilePath(root, x + 3, y + 3, seed, 128)))
            return new(x, y, 4, 4);
        return new(x, y, 3, 3);
    }

    private static byte[]? CreateReference(Layout layout, int seed, string root)
    {
        using var canvasImage = new SKBitmap(layout.Columns * ReferenceCellSize, layout.Rows * ReferenceCellSize);
        canvasImage.Erase(new SKColor(238, 238, 238));
        var found = false;
        using (var canvas = new SKCanvas(canvasImage))
        {
            for (var column = 0; column < layout.Columns; column++)
            for (var row = 0; row < layout.Rows; row++)
            {
                var path = TilePath(root, layout.OriginX + column, layout.OriginY + row, seed, 128);
                if (!IsAccepted(path)) continue;
                using var old = PngImages.Decode(path);
                canvas.DrawBitmap(old,
                    new SKRect(column * ReferenceCellSize, row * ReferenceCellSize,
                        (column + 1) * ReferenceCellSize, (row + 1) * ReferenceCellSize),
                    new SKSamplingOptions(SKFilterMode.Linear));
                found = true;
            }
        }
        if (!found) return null;
        using var data = canvasImage.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode the region reference.");
        return data.ToArray();
    }

    private static void SaveNew(SKBitmap image, string path)
    {
        if (IsAccepted(path)) return;
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            PngImages.Save(image, temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ConstrainAcceptedEdges(SKBitmap tile, int x, int y, int seed, string root,
        IReadOnlyCollection<(int X, int Y)> missing)
    {
        foreach (var (neighborX, neighborY, side) in new[]
        {
            (x - 1, y, 0), (x + 1, y, 1), (x, y - 1, 2), (x, y + 1, 3)
        })
        {
            if (missing.Contains((neighborX, neighborY))) continue;
            var neighborRegionX = (int)Math.Floor(neighborX / (double)RegionSize);
            var neighborRegionY = (int)Math.Floor(neighborY / (double)RegionSize);
            var hasRegionMarker = File.Exists(MarkerPath(root, neighborRegionX, neighborRegionY, seed));
            var highPath = TilePath(root, neighborX, neighborY, seed, 256);
            var lowPath = TilePath(root, neighborX, neighborY, seed, 128);
            if (!IsAccepted(lowPath)) continue;
            using var accepted = PngImages.Decode(hasRegionMarker && IsAccepted(highPath) ? highPath : lowPath);
            using var scaled = accepted.Width == 256 && accepted.Height == 256
                ? null : PngImages.Resize(accepted, 256, SKFilterMode.Linear);
            var neighbor = scaled ?? accepted;
            for (var line = 0; line < 256; line++)
            {
                for (var distance = 0; distance < 64; distance++)
                {
                    var position = side switch
                    {
                        0 => (distance, line),
                        1 => (255 - distance, line),
                        2 => (line, distance),
                        _ => (line, 255 - distance)
                    };
                    var from = side switch
                    {
                        0 => neighbor.GetPixel(255 - distance, line),
                        1 => neighbor.GetPixel(distance, line),
                        2 => neighbor.GetPixel(line, 255 - distance),
                        _ => neighbor.GetPixel(line, distance)
                    };
                    var color = tile.GetPixel(position.Item1, position.Item2);
                    var weight = 1f - distance / 64f;
                    tile.SetPixel(position.Item1, position.Item2, new SKColor(
                        Blend(color.Red, from.Red, weight),
                        Blend(color.Green, from.Green, weight),
                        Blend(color.Blue, from.Blue, weight), color.Alpha));
                }
            }
        }
    }

    private static byte Blend(byte generated, byte accepted, float weight)
        => (byte)Math.Clamp((int)Math.Round(generated * (1 - weight) + accepted * weight), 0, 255);

    private static bool IsAccepted(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var image = PngImages.Decode(path);
            return image.Width > 0 && image.Height > 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static int RegionSeed(int seed, int x, int y)
    {
        unchecked { return (((seed * 397) ^ x) * 397 ^ y) & int.MaxValue; }
    }

    private readonly record struct Layout(int OriginX, int OriginY, int Columns, int Rows)
    {
        public string AspectRatio => Columns == Rows ? "1:1" : $"{Columns}:{Rows}";
    }
}
