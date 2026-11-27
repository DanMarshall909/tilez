using SkiaSharp;

namespace Tilez;

public sealed record ZoomFrameRequest(string Prompt, int Seed, byte[]? ReferencePng, int Size = 512);

public interface IZoomFrameImageGenerator
{
    byte[] Generate(ZoomFrameRequest request);
}

public sealed class ZoomFrameRenderer(IZoomFrameImageGenerator backend)
{
    public const int RenderSize = 512;
    public const int DisplaySize = 256;
    public const int SourceCropSize = 410;

    public static string FramePath(string root, int seed, int depth)
    {
        if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth));
        return Path.Combine(root, $"seed_{seed}", "frames", $"frame_{depth:D4}.png");
    }

    public string Render(int depth, int seed, string outputRoot)
    {
        var path = FramePath(outputRoot, seed, depth);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var frameLock = AcquireLock(path + ".lock");
        if (IsAccepted(path)) return path;

        byte[]? reference = null;
        if (depth > 0)
        {
            var previousPath = FramePath(outputRoot, seed, depth - 1);
            if (!IsAccepted(previousPath))
                throw new InvalidOperationException($"Frame {depth - 1} must be accepted before frame {depth}.");
            using var previous = PngImages.Decode(previousPath);
            using var enlargedCenter = new SKBitmap(RenderSize, RenderSize);
            using (var canvas = new SKCanvas(enlargedCenter))
            {
                var inset = (RenderSize - SourceCropSize) / 2;
                canvas.DrawBitmap(previous,
                    new SKRect(inset, inset, inset + SourceCropSize, inset + SourceCropSize),
                    new SKRect(0, 0, RenderSize, RenderSize),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
            }
            using var encoded = enlargedCenter.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the center crop.");
            reference = encoded.ToArray();
        }

        var bytes = backend.Generate(new ZoomFrameRequest(PromptFor(depth), FrameSeed(seed, depth), reference));
        using var image = PngImages.Decode(bytes);
        if (image.Width != RenderSize || image.Height != RenderSize)
            throw new InvalidDataException($"Zoom frame must be {RenderSize}×{RenderSize} pixels.");
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
    }

    private static string PromptFor(int depth) => depth == 0
        ? "A single continuous top-down pixel art fantasy landscape, a mysterious clockwork forest with winding paths, mossy ruins and luminous streams. Crisp deliberate pixel art, coherent lighting, full bleed, no frame, border, panel, text or vignette."
        : $"Zoom slightly deeper into the exact center of the supplied fantasy landscape. Preserve the positions and shapes of its paths, streams, objects, colours and layout. Add many small, distinct pixel art details at this closer scale: individual stones, moss fronds, leaves, tiny gears and glowing traces. Resolve large flat pixel blocks into finer scene detail. Keep rivers, paths, coastlines and vegetation naturally irregular rather than rectangular. The supplied image is the composition to refine, not a new scene. Sharp deliberate small details, full bleed, no frame, border, panel, text or vignette.";

    private static int FrameSeed(int seed, int depth)
    {
        unchecked { return ((seed * 397) ^ depth) & int.MaxValue; }
    }

    private static bool IsAccepted(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var image = PngImages.Decode(path);
            return image.Width == RenderSize && image.Height == RenderSize;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static FileStream AcquireLock(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }
}
