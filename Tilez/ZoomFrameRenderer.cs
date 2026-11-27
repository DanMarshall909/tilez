using SkiaSharp;

namespace Tilez;

public sealed record ZoomFrameRequest(string Prompt, int Seed, byte[]? ReferencePng, int Size = 512)
{
    public byte[]? MaskPng { get; init; }
}

public interface IZoomFrameImageGenerator
{
    byte[] Generate(ZoomFrameRequest request);
}

public sealed class ZoomFrameRenderer(IZoomFrameImageGenerator backend)
{
    public const int RenderSize = 512;
    public const int DisplaySize = 256;
    public const int SourceCropSize = 256;
    public const int InnerDetailSize = 256;

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
        byte[]? mask = null;
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
                    new SKSamplingOptions(SKFilterMode.Nearest));
            }
            using var encoded = enlargedCenter.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the center crop.");
            reference = encoded.ToArray();

            using var detailMask = new SKBitmap(RenderSize, RenderSize);
            detailMask.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(detailMask))
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = false })
            {
                var detailInset = (RenderSize - InnerDetailSize) / 2;
                canvas.DrawRect(new SKRect(detailInset, detailInset,
                    detailInset + InnerDetailSize, detailInset + InnerDetailSize), paint);
            }
            using var encodedMask = detailMask.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the zoom detail mask.");
            mask = encodedMask.ToArray();
        }

        var bytes = backend.Generate(new ZoomFrameRequest(PromptFor(depth), FrameSeed(seed, depth), reference)
        {
            MaskPng = mask
        });
        using var image = PngImages.Decode(bytes);
        if (image.Width != RenderSize || image.Height != RenderSize)
            throw new InvalidDataException($"Zoom frame must be {RenderSize}×{RenderSize} pixels.");
        if (reference is not null)
        {
            using var source = PngImages.Decode(reference);
            using var composite = ComposeInnerDetail(source, image);
            using var encoded = composite.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the zoom frame.");
            bytes = encoded.ToArray();
        }
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
        ? "A single continuous top-down pixel-art steampunk cityscape: interlocking brass clockwork, copper pipes, winding railways, cobbled alleys, steam vents, iron bridges and warm glowing lamps. Rich amber and teal palette, crisp inked outlines, coherent lighting and readable shapes. Full bleed, no frame, border, panel, text or vignette."
        : $"Refine only the masked center half of this enlarged steampunk cityscape at depth {depth}. Treat the surrounding pixels as fixed context. Preserve the shapes and positions of brass mechanisms, copper pipes, winding railways, cobbled alleys and steam vents. Continue existing objects through the mask boundary, revealing fine clockwork and architectural details within the enlarged pixels. Match the existing top-down illustrated pixel-art style, inked contours and amber-teal palette. Keep natural contours irregular; avoid photographic blur. Do not introduce a new composition, frame, border, square patch, panel, text or vignette.";

    private static SKBitmap ComposeInnerDetail(SKBitmap reference, SKBitmap generated)
    {
        var result = new SKBitmap(RenderSize, RenderSize);
        using var generatedSmall = PngImages.Resize(generated, 64, SKFilterMode.Linear);
        using var generatedLow = PngImages.Resize(generatedSmall, RenderSize, SKFilterMode.Linear);
        var inset = (RenderSize - InnerDetailSize) / 2;
        var last = inset + InnerDetailSize - 1;
        const float feather = 16f;
        for (var y = 0; y < RenderSize; y++)
        {
            for (var x = 0; x < RenderSize; x++)
            {
                var source = reference.GetPixel(x, y);
                var edge = Math.Min(Math.Min(x - inset, last - x), Math.Min(y - inset, last - y));
                if (edge < 0)
                {
                    result.SetPixel(x, y, source);
                    continue;
                }
                var weight = Math.Clamp((edge + 1f) / feather, 0f, 1f);
                var detail = generated.GetPixel(x, y);
                var detailLow = generatedLow.GetPixel(x, y);
                result.SetPixel(x, y, new SKColor(
                    Lerp(source.Red, AddDetail(source.Red, detail.Red, detailLow.Red), weight),
                    Lerp(source.Green, AddDetail(source.Green, detail.Green, detailLow.Green), weight),
                    Lerp(source.Blue, AddDetail(source.Blue, detail.Blue, detailLow.Blue), weight),
                    Lerp(source.Alpha, detail.Alpha, weight)));
            }
        }
        return result;

        static byte Lerp(byte a, byte b, float t) =>
            (byte)Math.Clamp((int)MathF.Round(a + (b - a) * t), 0, 255);

        static byte AddDetail(byte source, byte detail, byte detailLow) =>
            (byte)Math.Clamp(source + detail - detailLow, 0, 255);
    }

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
