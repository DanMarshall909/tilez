using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    string UpscaleImage(string sourcePath, string outputRoot, int targetSize, string outputTag);
}

public sealed class CpuNearestNeighborUpscaler : IDisplayUpscaler
{
    public string BackendName => "cpu-nearest (GPU adapter point)";

    public string UpscaleImage(string sourcePath, string outputRoot, int targetSize, string outputTag)
    {
        using var image = Image.Load<Rgba32>(sourcePath);
        image.Mutate(ctx => ctx.Resize(targetSize, targetSize, KnownResamplers.NearestNeighbor));

        var scaledDir = Path.Combine(outputRoot, "images");
        Directory.CreateDirectory(scaledDir);
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + $"_{outputTag}_{targetSize}.png";
        var outputPath = Path.Combine(scaledDir, fileName);

        image.SaveAsPng(outputPath);
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
        var upscaled = _upscaler.UpscaleImage(lowRes, outputRoot, 128, "upscaled");

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
