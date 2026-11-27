using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class ZoomFrameRendererTests
{
    [Fact]
    public void High_resolution_zoom_keeps_every_source_and_output_pixel_at_1024()
    {
        using var temp = new ZoomDirectory();
        using var first = new SKBitmap(1024, 1024);
        first.Erase(SKColors.Black);
        first.SetPixel(256, 256, SKColors.Red);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));
        var backend = new RecordingZoomBackend(size: 1024);

        var path = new ZoomFrameRenderer(backend, 1024, ZoomCompositionMode.NestedScene).Render(1, 1337, temp.Path);

        Assert.Equal(1024, backend.Requests[0].Size);
        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        using var mask = SKBitmap.Decode(backend.Requests[0].MaskPng);
        using var saved = SKBitmap.Decode(path);
        Assert.NotNull(reference);
        Assert.NotNull(mask);
        Assert.NotNull(saved);
        Assert.Equal((1024, 1024), (saved.Width, saved.Height));
        Assert.Equal(SKColors.Red, reference.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, reference.GetPixel(1, 1));
        Assert.Equal(SKColors.Black, mask.GetPixel(0, 0));
        Assert.Equal(SKColors.White, mask.GetPixel(512, 512));
        Assert.Equal(reference.GetPixel(0, 0), saved.GetPixel(0, 0));
        Assert.Equal(reference.GetPixel(255, 512), saved.GetPixel(255, 512));
        Assert.Equal(SKColors.Purple, saved.GetPixel(512, 512));
        Assert.Contains("perspective", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("centered passage", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nested_zoom_begins_with_a_perspective_scene_leading_into_the_center()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend(size: 1024);

        new ZoomFrameRenderer(backend, 1024, ZoomCompositionMode.NestedScene).Render(0, 1337, temp.Path);

        Assert.Contains("perspective", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("central opening", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top-down", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Next_frame_uses_the_exact_center_of_the_accepted_image_at_twice_its_display_resolution()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        var renderer = new ZoomFrameRenderer(backend);
        using var first = new SKBitmap(512, 512);
        first.Erase(SKColors.Black);
        first.SetPixel(128, 128, SKColors.Red);
        first.SetPixel(383, 128, SKColors.Green);
        first.SetPixel(128, 383, SKColors.Blue);
        first.SetPixel(383, 383, SKColors.Yellow);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        var nextPath = renderer.Render(1, 1337, temp.Path);

        Assert.Equal(ZoomFrameRenderer.FramePath(temp.Path, 1337, 1), nextPath);
        Assert.Single(backend.Requests);
        Assert.Equal(512, backend.Requests[0].Size);
        Assert.Contains("center", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        Assert.NotNull(reference);
        Assert.Equal((512, 512), (reference.Width, reference.Height));
        Assert.Equal(SKColors.Red, reference.GetPixel(0, 0));
        Assert.Equal(SKColors.Green, reference.GetPixel(511, 0));
        Assert.Equal(SKColors.Blue, reference.GetPixel(0, 511));
        Assert.Equal(SKColors.Yellow, reference.GetPixel(511, 511));
        using var mask = SKBitmap.Decode(backend.Requests[0].MaskPng);
        Assert.NotNull(mask);
        Assert.Equal(SKColors.Black, mask.GetPixel(0, 0));
        Assert.Equal(SKColors.White, mask.GetPixel(256, 256));
        using var saved = SKBitmap.Decode(nextPath);
        Assert.NotNull(saved);
        Assert.Equal((512, 512), (saved.Width, saved.Height));
        Assert.Equal(SKColors.Red, saved.GetPixel(0, 0));
        Assert.Equal(reference.GetPixel(256, 256), saved.GetPixel(256, 256));
        Assert.Equal(reference.GetPixel(128, 256), saved.GetPixel(128, 256));
        for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
                if (x < 128 || x >= 384 || y < 128 || y >= 384)
                    Assert.Equal(reference.GetPixel(x, y), saved.GetPixel(x, y));
    }

    [Fact]
    public void Each_source_pixel_expands_to_a_two_by_two_patch_before_the_center_is_redrawn()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        using var first = new SKBitmap(512, 512);
        first.Erase(SKColors.Black);
        first.SetPixel(128, 128, SKColors.White);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        new ZoomFrameRenderer(backend).Render(1, 1337, temp.Path);

        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        Assert.NotNull(reference);
        Assert.Equal(SKColors.White, reference.GetPixel(0, 0));
        Assert.Equal(SKColors.White, reference.GetPixel(1, 0));
        Assert.Equal(SKColors.White, reference.GetPixel(0, 1));
        Assert.Equal(SKColors.White, reference.GetPixel(1, 1));
        Assert.Equal(SKColors.Black, reference.GetPixel(2, 0));
    }

    [Fact]
    public void Inner_half_reveals_small_detail_without_replacing_the_source_colour()
    {
        using var temp = new ZoomDirectory();
        using var first = new SKBitmap(512, 512);
        first.Erase(new SKColor(128, 128, 128));
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        var next = new ZoomFrameRenderer(new CheckerDetailBackend()).Render(1, 1337, temp.Path);

        using var image = SKBitmap.Decode(next);
        Assert.NotNull(image);
        Assert.Equal(new SKColor(128, 128, 128), image.GetPixel(10, 10));
        Assert.InRange(image.GetPixel(256, 256).Red, (byte)80, (byte)180);
        Assert.True(Math.Abs(image.GetPixel(256, 256).Red - image.GetPixel(260, 256).Red) > 30);
    }

    [Fact]
    public void Conflicting_model_colours_cannot_erase_an_existing_boundary()
    {
        using var temp = new ZoomDirectory();
        using var first = new SKBitmap(512, 512);
        for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
                first.SetPixel(x, y, x < 256 ? SKColors.Black : SKColors.White);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        var next = new ZoomFrameRenderer(new ConflictingBoundaryBackend()).Render(1, 1337, temp.Path);

        using var image = SKBitmap.Decode(next);
        Assert.NotNull(image);
        Assert.InRange(image.GetPixel(200, 256).Red, (byte)0, (byte)20);
        Assert.InRange(image.GetPixel(312, 256).Red, (byte)235, (byte)255);
    }

    [Fact]
    public void Accepted_frames_are_reused_without_another_generation_request()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        var renderer = new ZoomFrameRenderer(backend);

        var first = renderer.Render(0, 1337, temp.Path);
        var firstBytes = File.ReadAllBytes(first);
        renderer.Render(0, 1337, temp.Path);
        renderer.Render(1, 1337, temp.Path);
        renderer.Render(1, 1337, temp.Path);

        Assert.Equal(2, backend.Requests.Count);
        Assert.Null(backend.Requests[0].ReferencePng);
        Assert.NotNull(backend.Requests[1].ReferencePng);
        Assert.All(backend.Requests, request => Assert.Contains("steampunk", request.Prompt, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(backend.Requests[0].Prompt, backend.Requests[1].Prompt);
        Assert.Equal(firstBytes, File.ReadAllBytes(first));
    }

    [Fact]
    public void A_failed_or_wrong_sized_generation_does_not_publish_a_frame()
    {
        using var temp = new ZoomDirectory();
        var failing = new ZoomFrameRenderer(new RecordingZoomBackend(throwOnGenerate: true));
        Assert.Throws<InvalidOperationException>(() => failing.Render(0, 1337, temp.Path));
        Assert.False(File.Exists(ZoomFrameRenderer.FramePath(temp.Path, 1337, 0)));

        var wrongSize = new ZoomFrameRenderer(new RecordingZoomBackend(size: 256));
        Assert.Throws<InvalidDataException>(() => wrongSize.Render(0, 1337, temp.Path));
        Assert.False(File.Exists(ZoomFrameRenderer.FramePath(temp.Path, 1337, 0)));
    }

    [Fact]
    public void Different_seeds_keep_separate_accepted_frame_sequences()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        var renderer = new ZoomFrameRenderer(backend);

        var firstSeed = renderer.Render(0, 1337, temp.Path);
        var secondSeed = renderer.Render(0, 2048, temp.Path);
        renderer.Render(0, 1337, temp.Path);

        Assert.NotEqual(firstSeed, secondSeed);
        Assert.True(File.Exists(firstSeed));
        Assert.True(File.Exists(secondSeed));
        Assert.Equal(2, backend.Requests.Count);
        Assert.NotEqual(backend.Requests[0].Seed, backend.Requests[1].Seed);
    }

    private static void Save(SKBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        Assert.True(bitmap.Encode(stream, SKEncodedImageFormat.Png, 100));
    }

    private sealed class RecordingZoomBackend(int size = 512, bool throwOnGenerate = false) : IZoomFrameImageGenerator
    {
        public List<ZoomFrameRequest> Requests { get; } = [];

        public byte[] Generate(ZoomFrameRequest request)
        {
            Requests.Add(request);
            if (throwOnGenerate) throw new InvalidOperationException("Generation failed");
            using var image = new SKBitmap(size, size);
            image.Erase(SKColors.Purple);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
    }

    private sealed class CheckerDetailBackend : IZoomFrameImageGenerator
    {
        public byte[] Generate(ZoomFrameRequest request)
        {
            using var image = new SKBitmap(request.Size, request.Size);
            for (var y = 0; y < request.Size; y++)
                for (var x = 0; x < request.Size; x++)
                {
                    var shade = ((x / 4 + y / 4) & 1) == 0 ? (byte)160 : (byte)96;
                    image.SetPixel(x, y, new SKColor(shade, shade, shade));
                }
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
    }

    private sealed class ConflictingBoundaryBackend : IZoomFrameImageGenerator
    {
        public byte[] Generate(ZoomFrameRequest request)
        {
            using var image = new SKBitmap(request.Size, request.Size);
            for (var y = 0; y < request.Size; y++)
                for (var x = 0; x < request.Size; x++)
                    image.SetPixel(x, y, x < 256 ? SKColors.White : SKColors.Black);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
    }

    private sealed class ZoomDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tilez-zoom-" + Guid.NewGuid().ToString("N"));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
