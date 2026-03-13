using System.Globalization;
using System.Text;

namespace Tilez;

public sealed record TileRenderArtifacts(
    string MetadataPath,
    string LowResImagePath,
    string UpscaledImagePath,
    string? HighDetailImagePath,
    string UpscalerBackend);

public interface IDisplayUpscaler
{
    string BackendName { get; }

    string UpscalePpm(string sourcePath, string outputRoot, int targetSize, string outputTag);
}

public sealed class CpuNearestNeighborUpscaler : IDisplayUpscaler
{
    public string BackendName => "cpu-nearest (GPU adapter point)";

    public string UpscalePpm(string sourcePath, string outputRoot, int targetSize, string outputTag)
    {
        var image = PpmImage.Read(sourcePath);
        var scaled = image.ResizeNearest(targetSize, targetSize);

        var scaledDir = Path.Combine(outputRoot, "images");
        Directory.CreateDirectory(scaledDir);
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + $"_{outputTag}_{targetSize}.ppm";
        var outputPath = Path.Combine(scaledDir, fileName);

        scaled.Write(outputPath);
        return outputPath;
    }
}

public sealed class DisplayScalingPipeline
{
    private readonly SingleTileGenerator _generator;
    private readonly IDisplayUpscaler _upscaler;

    public DisplayScalingPipeline(SingleTileGenerator generator, IDisplayUpscaler upscaler)
    {
        _generator = generator;
        _upscaler = upscaler;
    }

    public TileRenderArtifacts RenderForDisplay(WorldTile tile, float zoomLevel, string outputRoot)
    {
        var metadataPath = TilePersistence.SaveMetadata(tile, outputRoot);
        var lowRes = _generator.RenderVariant(tile, outputRoot, 64, false, "low");
        var upscaled = _upscaler.UpscalePpm(lowRes, outputRoot, 128, "upscaled");

        string? highDetail = null;
        if (zoomLevel >= 2.5f)
        {
            highDetail = _generator.RenderVariant(tile, outputRoot, 256, true, "high");
        }

        return new TileRenderArtifacts(
            metadataPath,
            lowRes,
            upscaled,
            highDetail,
            _upscaler.BackendName);
    }
}

internal sealed class PpmImage
{
    private readonly (byte R, byte G, byte B)[,] _pixels;

    private PpmImage((byte R, byte G, byte B)[,] pixels)
    {
        _pixels = pixels;
    }

    public int Width => _pixels.GetLength(0);
    public int Height => _pixels.GetLength(1);

    public static PpmImage Read(string path)
    {
        var content = File.ReadAllText(path, Encoding.ASCII);
        var tokens = content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !token.StartsWith('#'))
            .ToList();

        if (tokens.Count < 4 || tokens[0] != "P3")
        {
            throw new InvalidOperationException("Unsupported PPM format.");
        }

        var width = int.Parse(tokens[1], CultureInfo.InvariantCulture);
        var height = int.Parse(tokens[2], CultureInfo.InvariantCulture);

        var pixels = new (byte R, byte G, byte B)[width, height];
        var index = 4;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = byte.Parse(tokens[index++], CultureInfo.InvariantCulture);
                var g = byte.Parse(tokens[index++], CultureInfo.InvariantCulture);
                var b = byte.Parse(tokens[index++], CultureInfo.InvariantCulture);
                pixels[x, y] = (r, g, b);
            }
        }

        return new PpmImage(pixels);
    }

    public PpmImage ResizeNearest(int targetWidth, int targetHeight)
    {
        var scaled = new (byte R, byte G, byte B)[targetWidth, targetHeight];

        for (var y = 0; y < targetHeight; y++)
        {
            var srcY = Math.Min((int)(y * (Height / (float)targetHeight)), Height - 1);
            for (var x = 0; x < targetWidth; x++)
            {
                var srcX = Math.Min((int)(x * (Width / (float)targetWidth)), Width - 1);
                scaled[x, y] = _pixels[srcX, srcY];
            }
        }

        return new PpmImage(scaled);
    }

    public void Write(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("P3");
        sb.AppendLine($"{Width} {Height}");
        sb.AppendLine("255");

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = _pixels[x, y];
                sb.Append(pixel.R.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(pixel.G.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(pixel.B.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }
}
