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

public enum ZoomCompositionMode { FineDetail, NestedScene }

public sealed class ZoomFrameRenderer(
    IZoomFrameImageGenerator backend,
    int renderSize = ZoomFrameRenderer.RenderSize,
    ZoomCompositionMode compositionMode = ZoomCompositionMode.FineDetail,
    string? cacheIdentity = null)
{
    public const int RenderSize = 512;
    public const int DisplaySize = 256;
    public const int SourceCropSize = 256;
    public const int InnerDetailSize = 256;

    private readonly int _renderSize = renderSize is 512 or 1024
        ? renderSize : throw new ArgumentOutOfRangeException(nameof(renderSize));

    public static string FramePath(string root, int seed, int depth)
    {
        if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth));
        return Path.Combine(root, $"seed_{seed}", "frames", $"frame_{depth:D4}.png");
    }

    public string Render(int depth, int seed, string outputRoot)
    {
        var path = FramePath(outputRoot, seed, depth);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureCacheIdentity(outputRoot, cacheIdentity);
        using var frameLock = AcquireLock(path + ".lock");
        if (IsAccepted(path, _renderSize)) return path;

        byte[]? reference = null;
        byte[]? mask = null;
        if (depth > 0)
        {
            var previousPath = FramePath(outputRoot, seed, depth - 1);
            if (!IsAccepted(previousPath, _renderSize))
                throw new InvalidOperationException($"Frame {depth - 1} must be accepted before frame {depth}.");
            using var previous = PngImages.Decode(previousPath);
            using var enlargedCenter = new SKBitmap(_renderSize, _renderSize);
            using (var canvas = new SKCanvas(enlargedCenter))
            {
                var sourceCrop = _renderSize / 2;
                var inset = (_renderSize - sourceCrop) / 2;
                canvas.DrawBitmap(previous,
                    new SKRect(inset, inset, inset + sourceCrop, inset + sourceCrop),
                    new SKRect(0, 0, _renderSize, _renderSize),
                    new SKSamplingOptions(SKFilterMode.Nearest));
            }
            using var encoded = enlargedCenter.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the center crop.");
            reference = encoded.ToArray();

            using var detailMask = new SKBitmap(_renderSize, _renderSize);
            detailMask.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(detailMask))
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = false })
            {
                var innerSize = _renderSize / 2;
                var detailInset = (_renderSize - innerSize) / 2;
                canvas.DrawRect(new SKRect(detailInset, detailInset,
                    detailInset + innerSize, detailInset + innerSize), paint);
            }
            using var encodedMask = detailMask.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the zoom detail mask.");
            mask = encodedMask.ToArray();
        }

        var bytes = backend.Generate(new ZoomFrameRequest(PromptFor(depth, compositionMode), FrameSeed(seed, depth), reference, _renderSize)
        {
            MaskPng = mask
        });
        using var image = PngImages.Decode(bytes);
        if (image.Width != _renderSize || image.Height != _renderSize)
            throw new InvalidDataException($"Zoom frame must be {_renderSize}×{_renderSize} pixels.");
        if (reference is not null)
        {
            using var source = PngImages.Decode(reference);
            using var composite = compositionMode == ZoomCompositionMode.NestedScene
                ? ComposeInnerScene(source, image, _renderSize)
                : ComposeInnerDetail(source, image, _renderSize);
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

    private static string PromptFor(int depth, ZoomCompositionMode compositionMode) => depth == 0
        ? compositionMode == ZoomCompositionMode.NestedScene
            ? "A single full-bleed illustrated steampunk perspective scene that leads straight into a central opening. An immense brass railway hall surrounds a luminous arched passage at the exact center of the page. Rails on the floor, copper pipes overhead, masonry and warm lamps converge naturally toward that opening. Through it, a smaller richly detailed clockwork chamber is already visible, inviting the viewer deeper. Keep the central passage within the middle half of the image so it can become the next scene. Rich amber and teal, crisp inked contours and deliberate pixel-art texture. One continuous environment with depth, not an aerial map. No inset picture, hard rectangular frame, panel, border, text or vignette."
            : "A single continuous top-down pixel-art steampunk cityscape: interlocking brass clockwork, copper pipes, winding railways, cobbled alleys, steam vents, iron bridges and warm glowing lamps. Rich amber and teal palette, crisp inked outlines, coherent lighting and readable shapes. Full bleed, no frame, border, panel, text or vignette."
        : compositionMode == ZoomCompositionMode.NestedScene
        ? $"Continue a seamless infinite zoom through the centered passage in the supplied steampunk perspective scene at depth {depth}. The supplied image is the exact surrounding composition. Inside only its central half, create the closer scene that appears beyond the opening: a richly detailed engine room, rail platform, clockwork gallery or architectural corridor suggested by what is already visible. Make rails, copper pipes, walls, light and perspective lines cross the central boundary continuously. Place another enticing centered passage or opening deeper inside the new scene so the next zoom has somewhere to go. The outer half is fixed context. Keep one continuous illustrated environment, amber-teal palette, crisp contours and fine pixel-art texture. No unrelated scene swap, inset picture, square patch, hard frame, panel, border, text or vignette."
        : $"Refine only the masked center half of this enlarged steampunk cityscape at depth {depth}. Treat the surrounding pixels as fixed context. Preserve the shapes and positions of brass mechanisms, copper pipes, winding railways, cobbled alleys and steam vents. Continue existing objects through the mask boundary, revealing fine clockwork and architectural details within the enlarged pixels. Match the existing top-down illustrated pixel-art style, inked contours and amber-teal palette. Keep natural contours irregular; avoid photographic blur. Do not introduce a new composition, frame, border, square patch, panel, text or vignette.";

    private static SKBitmap ComposeInnerScene(SKBitmap reference, SKBitmap generated, int size)
    {
        var result = new SKBitmap(size, size);
        var inset = size / 4;
        var last = size - inset - 1;
        var feather = size / 16f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var source = reference.GetPixel(x, y);
                var edge = Math.Min(Math.Min(x - inset, last - x), Math.Min(y - inset, last - y));
                if (edge < 0)
                {
                    result.SetPixel(x, y, source);
                    continue;
                }
                var t = Math.Clamp((edge + 1f) / feather, 0f, 1f);
                t = t * t * (3f - 2f * t);
                var detail = generated.GetPixel(x, y);
                result.SetPixel(x, y, new SKColor(
                    Lerp(source.Red, detail.Red, t),
                    Lerp(source.Green, detail.Green, t),
                    Lerp(source.Blue, detail.Blue, t),
                    Lerp(source.Alpha, detail.Alpha, t)));
            }
        return result;
    }

    private static SKBitmap ComposeInnerDetail(SKBitmap reference, SKBitmap generated, int size)
    {
        var result = new SKBitmap(size, size);
        using var generatedSmall = PngImages.Resize(generated, size / 8, SKFilterMode.Linear);
        using var generatedLow = PngImages.Resize(generatedSmall, size, SKFilterMode.Linear);
        var inset = size / 4;
        var last = size - inset - 1;
        var feather = size / 32f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
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

        static byte AddDetail(byte source, byte detail, byte detailLow) =>
            (byte)Math.Clamp(source + detail - detailLow, 0, 255);
    }

    private static byte Lerp(byte a, byte b, float t) =>
        (byte)Math.Clamp((int)MathF.Round(a + (b - a) * t), 0, 255);

    private static int FrameSeed(int seed, int depth)
    {
        unchecked { return ((seed * 397) ^ depth) & int.MaxValue; }
    }

    private static bool IsAccepted(string path, int size)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var image = PngImages.Decode(path);
            return image.Width == size && image.Height == size;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void EnsureCacheIdentity(string root, string? identity)
    {
        if (identity is null) return;
        using var identityLock = AcquireLock(Path.Combine(root, ".zoom-cache-identity.lock"));
        var marker = Path.Combine(root, ".zoom-cache-identity");
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != identity)
                throw new InvalidOperationException("This zoom output directory belongs to a different backend, model or generation setting.");
            return;
        }
        if (Directory.EnumerateFiles(root, "frame_*.png", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Existing zoom frames have no cache identity; select a fresh output directory.");
        var temporary = marker + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, identity);
            File.Move(temporary, marker);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
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
