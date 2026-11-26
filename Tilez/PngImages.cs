using SkiaSharp;

namespace Tilez;

internal static class PngImages
{
    public static SKBitmap Decode(byte[] bytes)
        => SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Image data could not be decoded.");

    public static SKBitmap Decode(string path)
        => SKBitmap.Decode(path) ?? throw new InvalidDataException($"Image could not be decoded: {path}");

    public static SKBitmap Resize(SKBitmap image, int size, SKFilterMode filter)
        => image.Resize(new SKSizeI(size, size), new SKSamplingOptions(filter))
            ?? throw new InvalidOperationException("Image resizing failed.");

    public static void Save(SKBitmap image, string path)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        using var output = File.Create(path);
        data.SaveTo(output);
    }
}
