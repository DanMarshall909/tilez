using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class RegionRendererTests
{
    [Fact]
    public void A_missing_three_by_three_region_uses_one_image_request_and_keeps_zoom_composition()
    {
        using var temp = new TempRegion();
        var root = temp.Path;
        var backend = new RecordingRegionBackend(SKColors.Blue);
        var renderer = new RegionRenderer(backend);

        renderer.Render(0, 0, 1337, root);

        Assert.Single(backend.Requests);
        Assert.Null(backend.Requests[0].ReferencePng);
        Assert.Equal("1:1", backend.Requests[0].AspectRatio);
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            using var low = SKBitmap.Decode(RegionRenderer.TilePath(root, x, y, 1337, 128));
            using var high = SKBitmap.Decode(RegionRenderer.TilePath(root, x, y, 1337, 256));
            Assert.NotNull(low);
            Assert.NotNull(high);
            Assert.Equal(SKColors.Blue, low.GetPixel(64, 64));
            Assert.Equal(SKColors.Blue, high.GetPixel(128, 128));
        }

        renderer.Render(0, 0, 1337, root);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public void Expansion_uses_existing_edge_as_a_spatial_reference_and_preserves_it()
    {
        using var temp = new TempRegion();
        var root = temp.Path;
        for (var y = 0; y < 3; y++)
        {
            var path = RegionRenderer.TilePath(root, 2, y, 1337, 128);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var old = new SKBitmap(128, 128);
            old.Erase(SKColors.Red);
            using var stream = File.Create(path);
            Assert.True(old.Encode(stream, SKEncodedImageFormat.Png, 100));
        }
        var oldBytes = File.ReadAllBytes(RegionRenderer.TilePath(root, 2, 0, 1337, 128));
        var backend = new RecordingRegionBackend(SKColors.Blue);

        new RegionRenderer(backend).Render(1, 0, 1337, root);

        Assert.Single(backend.Requests);
        Assert.Equal("4:3", backend.Requests[0].AspectRatio);
        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        using var mask = SKBitmap.Decode(backend.Requests[0].MaskPng);
        Assert.NotNull(reference);
        Assert.NotNull(mask);
        Assert.Equal(SKColors.Red, reference.GetPixel(128, 128));
        Assert.Equal(SKColors.Black, mask.GetPixel(128, 128));
        Assert.Equal(SKColors.White, mask.GetPixel(384, 128));
        Assert.Equal(oldBytes, File.ReadAllBytes(RegionRenderer.TilePath(root, 2, 0, 1337, 128)));
        Assert.True(File.Exists(RegionRenderer.TilePath(root, 3, 0, 1337, 128)));
        using var newTile = SKBitmap.Decode(RegionRenderer.TilePath(root, 3, 0, 1337, 256));
        Assert.NotNull(newTile);
        Assert.Equal(SKColors.Red, newTile.GetPixel(0, 128));
        Assert.Equal(SKColors.Blue, newTile.GetPixel(16, 128));
        Assert.Equal(SKColors.Blue, newTile.GetPixel(128, 128));
    }

    [Fact]
    public void Failed_region_generation_leaves_no_completed_artifacts_for_a_retry()
    {
        using var temp = new TempRegion();
        var root = temp.Path;
        var renderer = new RegionRenderer(new FailingRegionBackend());

        Assert.Throws<InvalidOperationException>(() => renderer.Render(-1, 0, 1337, root));

        Assert.False(File.Exists(RegionRenderer.MarkerPath(root, -1, 0, 1337)));
        Assert.False(File.Exists(RegionRenderer.TilePath(root, -3, 0, 1337, 128)));
    }

    [Fact]
    public void Corrupt_cached_tile_is_regenerated_instead_of_becoming_a_reference()
    {
        using var temp = new TempRegion();
        var root = temp.Path;
        var path = RegionRenderer.TilePath(root, 0, 0, 1337, 128);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a PNG");
        var backend = new RecordingRegionBackend(SKColors.Blue);

        new RegionRenderer(backend).Render(0, 0, 1337, root);

        Assert.Single(backend.Requests);
        Assert.Null(backend.Requests[0].ReferencePng);
        using var image = SKBitmap.Decode(path);
        Assert.NotNull(image);
        Assert.Equal(SKColors.Blue, image.GetPixel(64, 64));
    }

    [Fact]
    public void Diagonal_expansion_still_uses_the_existing_world_as_a_reference()
    {
        using var temp = new TempRegion();
        var root = temp.Path;
        var path = RegionRenderer.TilePath(root, -1, -1, 1337, 128);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var old = new SKBitmap(128, 128))
        {
            old.Erase(SKColors.Red);
            using var stream = File.Create(path);
            Assert.True(old.Encode(stream, SKEncodedImageFormat.Png, 100));
        }
        var backend = new RecordingRegionBackend(SKColors.Blue);

        new RegionRenderer(backend).Render(0, 0, 1337, root);

        Assert.Single(backend.Requests);
        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        Assert.NotNull(reference);
        Assert.Equal(SKColors.Red, reference.GetPixel(128, 128));
        Assert.True(File.Exists(RegionRenderer.TilePath(root, 0, 0, 1337, 128)));
    }

    [Fact]
    public void A_missing_low_resolution_derivative_is_rebuilt_from_accepted_artwork()
    {
        using var temp = new TempRegion();
        var backend = new RecordingRegionBackend(SKColors.Blue);
        var renderer = new RegionRenderer(backend);
        renderer.Render(0, 0, 1337, temp.Path);
        var highPath = RegionRenderer.TilePath(temp.Path, 0, 0, 1337, 256);
        var highBytes = File.ReadAllBytes(highPath);
        File.Delete(RegionRenderer.TilePath(temp.Path, 0, 0, 1337, 128));

        renderer.Render(0, 0, 1337, temp.Path);

        Assert.Single(backend.Requests);
        Assert.Equal(highBytes, File.ReadAllBytes(highPath));
        using var low = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, 0, 0, 1337, 128));
        Assert.NotNull(low);
        Assert.Equal(SKColors.Blue, low.GetPixel(64, 64));
    }

    [Fact]
    public async Task Concurrent_renderers_publish_one_consistent_region()
    {
        using var temp = new TempRegion();
        var backend = new SlowRegionBackend();
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            new RegionRenderer(backend).Render(0, 0, 1337, temp.Path)));

        await Task.WhenAll(tasks);

        Assert.Equal(1, backend.RequestCount);
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            using var low = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, x, y, 1337, 128));
            using var high = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, x, y, 1337, 256));
            Assert.NotNull(low);
            Assert.NotNull(high);
            Assert.Equal(low.GetPixel(64, 64), high.GetPixel(128, 128));
        }
    }

    [Fact]
    public void A_model_drawn_outer_frame_is_removed_before_tiles_are_accepted()
    {
        using var temp = new TempRegion();
        new RegionRenderer(new FramedRegionBackend()).Render(0, 0, 1337, temp.Path);

        using var topLeft = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, 0, 0, 1337, 256));
        using var bottomRight = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, 2, 2, 1337, 256));
        Assert.NotNull(topLeft);
        Assert.NotNull(bottomRight);
        Assert.Equal(SKColors.Blue, topLeft.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, bottomRight.GetPixel(255, 255));
    }

    [Fact]
    public void Local_resolution_scales_the_model_request_and_spatial_reference()
    {
        using var temp = new TempRegion();
        var backend = new RecordingRegionBackend(SKColors.Blue);
        var renderer = new RegionRenderer(backend, 192);

        renderer.Render(0, 0, 1337, temp.Path);
        renderer.Render(1, 0, 1337, temp.Path);

        Assert.Equal(2, backend.Requests.Count);
        Assert.All(backend.Requests, request => Assert.Equal(192, request.CellSize));
        using var reference = SKBitmap.Decode(backend.Requests[1].ReferencePng);
        using var mask = SKBitmap.Decode(backend.Requests[1].MaskPng);
        Assert.NotNull(reference);
        Assert.NotNull(mask);
        Assert.Equal((768, 576), (reference.Width, reference.Height));
        Assert.Equal((768, 576), (mask.Width, mask.Height));
    }

    [Fact]
    public void Region_request_describes_each_position_with_coordinate_specific_features()
    {
        using var first = new TempRegion();
        using var second = new TempRegion();
        var backend = new RecordingRegionBackend(SKColors.Blue);
        var renderer = new RegionRenderer(backend, 128);
        var authoredBiomes = new List<string>();
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        {
            var biome = $"biome {x} {y}";
            authoredBiomes.Add(biome);
            var features = new PromptFeatures(biome, "tranquil",
                new Dictionary<string, float> { ["roots"] = 1 },
                new Dictionary<string, float> { ["stone"] = 1 },
                new Dictionary<string, float> { ["teal"] = 1 });
            TilePersistence.SaveMetadata(new WorldTile(new TileCoordinate(x, y), 1337, features, "authored", ""), first.Path);
        }

        renderer.Render(0, 0, 1337, first.Path);
        renderer.Render(1, 0, 1337, second.Path);

        Assert.Equal(2, backend.Requests.Count);
        foreach (var biome in authoredBiomes) Assert.Contains(biome, backend.Requests[0].Prompt);
        Assert.NotEqual(backend.Requests[0].Prompt, backend.Requests[1].Prompt);
        Assert.True(File.Exists(System.IO.Path.Combine(first.Path, "metadata", "tile_0_0_1337.json")));
    }

    [Fact]
    public void Authored_neighbor_changes_the_expansion_request_without_replacing_metadata()
    {
        using var first = new TempRegion();
        using var second = new TempRegion();
        var backend = new RecordingRegionBackend(SKColors.Blue);
        var renderer = new RegionRenderer(backend, 128);
        var coordinate = new TileCoordinate(2, 0);
        var authoredBiome = new[] { "fungal bloom", "glass desert" };
        var roots = new[] { first.Path, second.Path };
        var originals = new byte[2][];
        for (var i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            var features = new PromptFeatures(authoredBiome[i], "tranquil",
                new Dictionary<string, float> { ["roots"] = 1 },
                new Dictionary<string, float> { ["stone"] = 1 },
                new Dictionary<string, float> { ["teal"] = 1 });
            var metadataPath = TilePersistence.SaveMetadata(new WorldTile(coordinate, 1337, features, "authored", ""), root);
            originals[i] = File.ReadAllBytes(metadataPath);
            var acceptedPath = RegionRenderer.TilePath(root, 2, 0, 1337, 128);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(acceptedPath)!);
            using var accepted = new SKBitmap(128, 128);
            accepted.Erase(SKColors.Green);
            using var stream = File.Create(acceptedPath);
            Assert.True(accepted.Encode(stream, SKEncodedImageFormat.Png, 100));
        }

        renderer.Render(1, 0, 1337, first.Path);
        renderer.Render(1, 0, 1337, second.Path);

        Assert.Contains("fungal bloom", backend.Requests[0].Prompt);
        Assert.Contains("glass desert", backend.Requests[1].Prompt);
        Assert.NotEqual(backend.Requests[0].Prompt, backend.Requests[1].Prompt);
        for (var i = 0; i < roots.Length; i++)
            Assert.Equal(originals[i], File.ReadAllBytes(System.IO.Path.Combine(roots[i], "metadata", "tile_2_0_1337.json")));
        Assert.Equal(2, backend.Requests.Count);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    public void Outpainted_road_keeps_its_reference_position_at_the_shared_edge(int cellSize)
    {
        using var temp = new TempRegion();
        var oldPath = RegionRenderer.TilePath(temp.Path, 2, 0, 1337, 128);
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        using (var old = new SKBitmap(128, 128))
        {
            old.Erase(SKColors.White);
            using var canvas = new SKCanvas(old);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(0, 30, 128, 34), paint);
            using var stream = File.Create(oldPath);
            Assert.True(old.Encode(stream, SKEncodedImageFormat.Png, 100));
        }

        new RegionRenderer(new RoadContinuationBackend(), cellSize).Render(1, 0, 1337, temp.Path);

        using var next = SKBitmap.Decode(RegionRenderer.TilePath(temp.Path, 3, 0, 1337, 256));
        Assert.NotNull(next);
        Assert.Equal(SKColors.Red, next.GetPixel(32, 64));
        Assert.Equal(SKColors.White, next.GetPixel(32, 50));
    }

    private sealed class FailingRegionBackend : IRegionImageGenerator
    {
        public byte[] Generate(RegionImageRequest request) => throw new InvalidOperationException("request failed");
    }

    private sealed class TempRegion : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tilez-region-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingRegionBackend(SKColor fill) : IRegionImageGenerator
    {
        public List<RegionImageRequest> Requests { get; } = [];

        public byte[] Generate(RegionImageRequest request)
        {
            Requests.Add(request);
            var width = request.AspectRatio == "4:3" ? 1200 : 900;
            var height = request.AspectRatio == "3:4" ? 1200 : 900;
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(fill);
            using var stream = new MemoryStream();
            Assert.True(bitmap.Encode(stream, SKEncodedImageFormat.Png, 100));
            return stream.ToArray();
        }
    }

    private sealed class SlowRegionBackend : IRegionImageGenerator
    {
        public int RequestCount;

        public byte[] Generate(RegionImageRequest request)
        {
            Interlocked.Increment(ref RequestCount);
            Thread.Sleep(100);
            using var bitmap = new SKBitmap(900, 900);
            bitmap.Erase(SKColors.Blue);
            using var stream = new MemoryStream();
            if (!bitmap.Encode(stream, SKEncodedImageFormat.Png, 100))
                throw new InvalidOperationException("Could not encode test image.");
            return stream.ToArray();
        }
    }

    private sealed class FramedRegionBackend : IRegionImageGenerator
    {
        public byte[] Generate(RegionImageRequest request)
        {
            using var bitmap = new SKBitmap(900, 900);
            bitmap.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = SKColors.Blue })
                canvas.DrawRect(new SKRect(12, 12, 888, 888), paint);
            using var stream = new MemoryStream();
            if (!bitmap.Encode(stream, SKEncodedImageFormat.Png, 100))
                throw new InvalidOperationException("Could not encode test image.");
            return stream.ToArray();
        }
    }

    private sealed class RoadContinuationBackend : IRegionImageGenerator
    {
        public byte[] Generate(RegionImageRequest request)
        {
            Assert.NotNull(request.ReferencePng);
            using var bitmap = new SKBitmap(4 * request.CellSize, 3 * request.CellSize);
            bitmap.Erase(SKColors.White);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.Red };
            var roadY = request.CellSize / 4f;
            canvas.DrawRect(new SKRect(request.CellSize, roadY - 2,
                bitmap.Width, roadY + 2), paint);
            using var stream = new MemoryStream();
            Assert.True(bitmap.Encode(stream, SKEncodedImageFormat.Png, 100));
            return stream.ToArray();
        }
    }
}
