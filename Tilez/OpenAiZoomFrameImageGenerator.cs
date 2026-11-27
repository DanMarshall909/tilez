using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace Tilez;

public sealed record OpenAiZoomImageOptions(string Model, string ApiKey, string Quality);

public sealed class OpenAiZoomFrameImageGenerator : IZoomFrameImageGenerator
{
    private readonly OpenAiZoomImageOptions _options;
    private readonly HttpClient _client;
    public string CacheIdentity => $"openai-zoom|{_options.Model}|{_options.Quality}|nested-v2|1024";

    public OpenAiZoomFrameImageGenerator(OpenAiZoomImageOptions options, HttpClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("OPENAI_API_KEY is required for OpenAI zoom generation.", nameof(options));
        if (options.Quality is not ("low" or "medium" or "high" or "xhigh" or "max"))
            throw new ArgumentException("OpenAI image quality must be low, medium, high, xhigh or max.", nameof(options));
        _options = options;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
    }

    public byte[] Generate(ZoomFrameRequest request)
    {
        if (request.Size != 1024)
            throw new ArgumentOutOfRangeException(nameof(request), "OpenAI zoom frames must be 1024 pixels square.");
        if ((request.ReferencePng is null) != (request.MaskPng is null))
            throw new ArgumentException("A zoom edit requires both a reference and a mask.", nameof(request));

        var editing = request.ReferencePng is not null;
        using var message = new HttpRequestMessage(HttpMethod.Post,
            editing ? "https://api.openai.com/v1/images/edits" : "https://api.openai.com/v1/images/generations");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        var fields = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["prompt"] = request.Prompt,
            ["size"] = $"{request.Size}x{request.Size}",
            ["quality"] = _options.Quality,
            ["output_format"] = "png",
            ["n"] = 1
        };
        if (editing)
        {
            using var reference = PngImages.Decode(request.ReferencePng!);
            using var mask = PngImages.Decode(request.MaskPng!);
            if (reference.Width != request.Size || reference.Height != request.Size
                || mask.Width != request.Size || mask.Height != request.Size)
                throw new InvalidDataException("OpenAI zoom reference and mask must both be 1024 pixels square.");

            var form = new MultipartFormDataContent();
            foreach (var (name, value) in fields)
                form.Add(new StringContent(value.ToString()!), name);
            form.Add(PngPart(request.ReferencePng!), "image", "reference.png");
            form.Add(PngPart(TransparentEditMask(mask)), "mask", "mask.png");
            message.Content = form;
        }
        else
            message.Content = new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");

        using var response = _client.Send(message);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI image request failed ({(int)response.StatusCode} {ErrorCode(body)}).");
        using var parsed = JsonDocument.Parse(body);
        if (!parsed.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("b64_json", out var image))
            throw new InvalidDataException("OpenAI image response did not contain PNG bytes.");
        return Convert.FromBase64String(image.GetString() ?? "");
    }

    private static ByteArrayContent PngPart(byte[] png)
    {
        var content = new ByteArrayContent(png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static byte[] TransparentEditMask(SKBitmap source)
    {
        using var result = new SKBitmap(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                result.SetPixel(x, y, new SKColor(0, 0, 0, source.GetPixel(x, y).Red > 127 ? (byte)0 : (byte)255));
        using var encoded = result.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode the OpenAI edit mask.");
        return encoded.ToArray();
    }

    private static string ErrorCode(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            var code = parsed.RootElement.GetProperty("error").GetProperty("code").GetString();
            return string.IsNullOrWhiteSpace(code) ? "unknown_error" : code;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "unknown_error";
        }
    }
}
