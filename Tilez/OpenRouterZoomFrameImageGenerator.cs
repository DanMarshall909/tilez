using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Tilez;

public sealed record OpenRouterZoomImageOptions(string Model, string ApiKey, string Quality);

public sealed class OpenRouterZoomFrameImageGenerator : IZoomFrameImageGenerator
{
    private readonly OpenRouterZoomImageOptions _options;
    private readonly HttpClient _client;
    public string CacheIdentity => $"openrouter-zoom|{_options.Model}|{_options.Quality}|nested-v2|1024";

    public OpenRouterZoomFrameImageGenerator(OpenRouterZoomImageOptions options, HttpClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("OPENROUTER_API_KEY is required for OpenRouter zoom generation.", nameof(options));
        if (options.Quality is not ("low" or "medium" or "high" or "xhigh" or "max"))
            throw new ArgumentException("OpenRouter image quality must be low, medium, high, xhigh or max.", nameof(options));
        _options = options;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
    }

    public byte[] Generate(ZoomFrameRequest request)
    {
        if (request.Size != 1024)
            throw new ArgumentOutOfRangeException(nameof(request), "OpenAI models on OpenRouter require 1024-pixel zoom frames.");
        if ((request.ReferencePng is null) != (request.MaskPng is null))
            throw new ArgumentException("A zoom edit requires both a reference and a center mask.", nameof(request));

        var payload = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["prompt"] = request.Prompt,
            ["size"] = "1024x1024",
            ["quality"] = _options.Quality,
            ["output_format"] = "png",
            ["n"] = 1
        };
        if (request.ReferencePng is not null)
        {
            using var reference = PngImages.Decode(request.ReferencePng);
            using var mask = PngImages.Decode(request.MaskPng!);
            if (reference.Width != 1024 || reference.Height != 1024 || mask.Width != 1024 || mask.Height != 1024)
                throw new InvalidDataException("OpenRouter zoom reference and mask must both be 1024 pixels square.");
            // OpenRouter exposes image references, but not the provider's edit-mask parameter.
            // ZoomFrameRenderer still enforces the exact outer pixels after this request.
            payload["input_references"] = new[]
            {
                new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(request.ReferencePng) } }
            };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/images")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        using var response = _client.Send(message);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenRouter zoom request failed with HTTP {(int)response.StatusCode}.");
        using var parsed = JsonDocument.Parse(body);
        if (parsed.RootElement.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("cost", out var cost)
            && cost.TryGetDecimal(out var usd))
            Log.Information("OpenRouter zoom request cost: USD {Cost}", usd);
        if (!parsed.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("b64_json", out var image)
            || string.IsNullOrWhiteSpace(image.GetString()))
            throw new InvalidDataException("OpenRouter image response did not contain PNG bytes.");
        return Convert.FromBase64String(image.GetString()!);
    }
}
