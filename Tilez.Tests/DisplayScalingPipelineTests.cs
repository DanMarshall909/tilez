using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class DisplayScalingPipelineTests
{
    [Fact]
    public void Cpu_upscaler_preserves_nearest_neighbor_pixels()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"tilez-scale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var sourcePath = Path.Combine(outputRoot, "source.png");
        using (var source = new SKBitmap(2, 2))
        {
            source.SetPixel(0, 0, SKColors.Red);
            source.SetPixel(1, 0, SKColors.Lime);
            source.SetPixel(0, 1, SKColors.Blue);
            source.SetPixel(1, 1, SKColors.Yellow);
            using var file = File.Create(sourcePath);
            Assert.True(source.Encode(file, SKEncodedImageFormat.Png, 100));
        }

        var outputPath = new CpuNearestNeighborUpscaler().UpscaleImage(sourcePath, outputRoot, 4, "upscaled");

        using var output = SKBitmap.Decode(outputPath);
        Assert.NotNull(output);
        Assert.Equal(SKColors.Red, output.GetPixel(1, 1));
        Assert.Equal(SKColors.Lime, output.GetPixel(2, 1));
        Assert.Equal(SKColors.Blue, output.GetPixel(1, 2));
        Assert.Equal(SKColors.Yellow, output.GetPixel(2, 2));
    }

    [Fact]
    public void Normal_zoom_reuses_the_base_image_without_a_second_paid_generation()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"tilez-render-{Guid.NewGuid():N}");
        var backend = new RecordingGenerator();
        var generator = new SingleTileGenerator(backend);
        var tile = generator.Generate(new TileCoordinate(0, 0), 1337, outputRoot);
        var pipeline = new DisplayScalingPipeline(generator, new CpuNearestNeighborUpscaler());

        var artifacts = pipeline.RenderForDisplay(tile, 1.0f, outputRoot);

        Assert.Equal(1, backend.RequestCount);
        Assert.Equal(tile.ImagePath, artifacts.LowResImagePath);
        Assert.True(File.Exists(artifacts.UpscaledImagePath));
    }

    private sealed class RecordingGenerator : ITileImageGenerator
    {
        public string BackendName => "recording";
        public int RequestCount { get; private set; }

        public void Generate(TileImageRequest request)
        {
            RequestCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            using var image = new SKBitmap(request.Size, request.Size);
            using var file = File.Create(request.OutputPath);
            Assert.True(image.Encode(file, SKEncodedImageFormat.Png, 100));
        }
    }
}
