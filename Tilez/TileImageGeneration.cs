using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Tilez;

public sealed record TileImageRequest(
    TileCoordinate Coordinate,
    int Seed,
    PromptFeatures Features,
    int Size,
    bool HighDetail,
    string Tag,
    string OutputPath);

public interface ITileImageGenerator
{
    string BackendName { get; }

    void Generate(TileImageRequest request);
}

public sealed class ProceduralTileImageGenerator : ITileImageGenerator
{
    public string BackendName => "procedural";

    public void Generate(TileImageRequest request)
    {
        var color = PaletteToRgb(request.Features.PaletteWeights);
        var frequency = request.HighDetail ? 0.45f : 0.2f;

        using var image = new Image<Rgba32>(request.Size, request.Size);

        for (var y = 0; y < request.Size; y++)
        {
            for (var x = 0; x < request.Size; x++)
            {
                var wave = (float)(Math.Sin((x + y) * frequency) * 0.15 + 0.85);
                var r = (byte)Math.Clamp((int)(color.R * wave), 0, 255);
                var g = (byte)Math.Clamp((int)(color.G * wave), 0, 255);
                var b = (byte)Math.Clamp((int)(color.B * wave), 0, 255);
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        image.SaveAsPng(request.OutputPath);
    }

    private static (int R, int G, int B) PaletteToRgb(IReadOnlyDictionary<string, float> paletteWeights)
    {
        var amber = paletteWeights.GetValueOrDefault("amber");
        var verdigris = paletteWeights.GetValueOrDefault("verdigris");
        var violet = paletteWeights.GetValueOrDefault("violet");
        var teal = paletteWeights.GetValueOrDefault("teal");
        var rust = paletteWeights.GetValueOrDefault("rust");
        var ivory = paletteWeights.GetValueOrDefault("ivory");

        var r = Clamp01(amber * 0.9f + rust * 0.8f + ivory * 0.7f + violet * 0.3f);
        var g = Clamp01(verdigris * 0.9f + teal * 0.7f + ivory * 0.6f);
        var b = Clamp01(violet * 0.9f + teal * 0.6f + ivory * 0.5f);

        return ((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}

public sealed record OpenAiImageOptions(string BaseUrl, string Model, string? ApiKey);

public sealed class OpenAiCompatibleTileImageGenerator : ITileImageGenerator
{
    private readonly OpenAiImageOptions _options;
    private readonly ITileImageGenerator _fallback;

    public OpenAiCompatibleTileImageGenerator(OpenAiImageOptions options, ITileImageGenerator? fallback = null)
    {
        _options = options;
        _fallback = fallback ?? new ProceduralTileImageGenerator();
    }

    public string BackendName => $"openai-compatible ({_options.BaseUrl})";

    public void Generate(TileImageRequest request)
    {
        try
        {
            GenerateWithApi(request);
        }
        catch
        {
            _fallback.Generate(request);
        }
    }

    private void GenerateWithApi(TileImageRequest request)
    {
        var prompt = BuildPrompt(request);
        var endpoint = BuildEndpoint(_options.BaseUrl);
        var payload = BuildPayload(prompt, request.Size);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = client.Send(message);
        response.EnsureSuccessStatusCode();

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var bytes = ExtractImageBytes(json);
        File.WriteAllBytes(request.OutputPath, bytes);
    }

    private static string BuildPrompt(TileImageRequest request)
    {
        var biome = request.Features.Biome;
        var mood = request.Features.Mood;
        var motif = TopKey(request.Features.MotifWeights);
        var material = TopKey(request.Features.MaterialWeights);
        var palette = TopKey(request.Features.PaletteWeights);
        var detail = request.HighDetail ? "high detail" : "medium detail";
        return $"top-down world tile, {biome}, {mood}, {material}, {motif}, {palette} palette, {detail}, game art";
    }

    private static string BuildEndpoint(string baseUrl)
    {
        return baseUrl.TrimEnd('/') + "/v1/images/generations";
    }

    private string BuildPayload(string prompt, int size)
    {
        var clamped = Math.Clamp(size, 64, 1024);
        var nearest = clamped >= 1024 ? 1024 : clamped >= 512 ? 512 : 256;
        var sizeText = $"{nearest}x{nearest}";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["prompt"] = prompt,
            ["n"] = 1,
            ["size"] = sizeText,
            ["response_format"] = "b64_json"
        };

        return JsonSerializer.Serialize(payload);
    }

    private static byte[] ExtractImageBytes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0)
        {
            var first = data[0];
            if (first.TryGetProperty("b64_json", out var b64))
            {
                var value = b64.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return Convert.FromBase64String(value);
                }
            }
        }

        throw new InvalidOperationException("Image bytes not found in API response.");
    }

    private static string TopKey(IReadOnlyDictionary<string, float> values)
    {
        return values.Count == 0 ? "neutral" : values.OrderByDescending(pair => pair.Value).First().Key;
    }
}
