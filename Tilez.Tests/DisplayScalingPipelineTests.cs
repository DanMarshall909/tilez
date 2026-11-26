using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tilez;

namespace Tilez.Tests;

public sealed class DisplayScalingPipelineTests
{
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
            using var image = new Image<Rgba32>(request.Size, request.Size);
            image.SaveAsPng(request.OutputPath);
        }
    }
}
