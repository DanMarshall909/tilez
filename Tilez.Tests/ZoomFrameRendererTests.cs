using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class ZoomFrameRendererTests
{
    [Fact]
    public void Next_frame_uses_the_exact_center_of_the_accepted_image_at_twice_its_display_resolution()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        var renderer = new ZoomFrameRenderer(backend);
        using var first = new SKBitmap(512, 512);
        first.Erase(SKColors.Black);
        first.SetPixel(51, 51, SKColors.Red);
        first.SetPixel(460, 51, SKColors.Green);
        first.SetPixel(51, 460, SKColors.Blue);
        first.SetPixel(460, 460, SKColors.Yellow);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        var nextPath = renderer.Render(1, 1337, temp.Path);

        Assert.Equal(ZoomFrameRenderer.FramePath(temp.Path, 1337, 1), nextPath);
        Assert.Single(backend.Requests);
        Assert.Equal(512, backend.Requests[0].Size);
        Assert.Contains("center", backend.Requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        Assert.NotNull(reference);
        Assert.Equal((512, 512), (reference.Width, reference.Height));
        Assert.True(reference.GetPixel(0, 0).Red > 150);
        Assert.True(reference.GetPixel(511, 0).Green > 80);
        Assert.True(reference.GetPixel(0, 511).Blue > 150);
        Assert.True(reference.GetPixel(511, 511).Red > 150 && reference.GetPixel(511, 511).Green > 150);
        using var saved = SKBitmap.Decode(nextPath);
        Assert.NotNull(saved);
        Assert.Equal((512, 512), (saved.Width, saved.Height));
    }

    [Fact]
    public void Enlarged_reference_softens_hard_pixel_steps_before_diffusion()
    {
        using var temp = new ZoomDirectory();
        var backend = new RecordingZoomBackend();
        using var first = new SKBitmap(512, 512);
        first.Erase(SKColors.Black);
        for (var y = 0; y < 512; y++)
            for (var x = 256; x < 512; x++)
                first.SetPixel(x, y, SKColors.White);
        Save(first, ZoomFrameRenderer.FramePath(temp.Path, 1337, 0));

        new ZoomFrameRenderer(backend).Render(1, 1337, temp.Path);

        using var reference = SKBitmap.Decode(backend.Requests[0].ReferencePng);
        Assert.NotNull(reference);
        Assert.Contains(Enumerable.Range(252, 8), x =>
            reference.GetPixel(x, 256).Red is > 16 and < 239);
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

    private sealed class ZoomDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tilez-zoom-" + Guid.NewGuid().ToString("N"));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
