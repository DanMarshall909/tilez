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
        Assert.NotNull(reference);
        Assert.Equal(SKColors.Red, reference.GetPixel(128, 128));
        Assert.Equal(oldBytes, File.ReadAllBytes(RegionRenderer.TilePath(root, 2, 0, 1337, 128)));
        Assert.True(File.Exists(RegionRenderer.TilePath(root, 3, 0, 1337, 128)));
        using var newTile = SKBitmap.Decode(RegionRenderer.TilePath(root, 3, 0, 1337, 256));
        Assert.NotNull(newTile);
        Assert.Equal(SKColors.Red, newTile.GetPixel(0, 128));
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
}
