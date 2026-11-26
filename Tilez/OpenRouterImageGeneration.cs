using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Tilez;

public sealed class OpenRouterImageOptions
{
    public string Model { get; }
    internal string ApiKey { get; }

    public OpenRouterImageOptions(string model, string apiKey)
    {
        Model = !string.IsNullOrWhiteSpace(model) ? model : throw new ArgumentException("An image model is required.", nameof(model));
        ApiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : throw new ArgumentException("OPENROUTER_API_KEY is required.", nameof(apiKey));
    }
}

public sealed class OpenRouterTileImageGenerator : ITileImageGenerator
{
    private static readonly Uri Endpoint = new("https://openrouter.ai/api/v1/images");
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly OpenRouterImageOptions _options;
    private readonly HttpClient _client;

    public OpenRouterTileImageGenerator(OpenRouterImageOptions options, HttpClient? client = null)
    {
        _options = options;
        _client = client ?? SharedClient;
    }

    public string BackendName => $"openrouter ({_options.Model})";

    public void Generate(TileImageRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = _options.Model,
            prompt = BuildPrompt(request),
            aspect_ratio = "1:1",
            output_format = "png",
            seed = CoordinateSeed(request)
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = _client.Send(message);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenRouter image request failed with HTTP {(int)response.StatusCode}.");
        }

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("b64_json", out var encoded)
            || string.IsNullOrWhiteSpace(encoded.GetString()))
        {
            throw new InvalidOperationException("OpenRouter returned no image data.");
        }

        var bytes = Convert.FromBase64String(encoded.GetString()!);
        using var image = Image.Load(bytes);
        if (image.Width != request.Size || image.Height != request.Size)
            image.Mutate(context => context.Resize(request.Size, request.Size));
        var directory = Path.GetDirectoryName(request.OutputPath)
            ?? throw new ArgumentException("An output directory is required.", nameof(request));
        Directory.CreateDirectory(directory);
        var temporaryPath = request.OutputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            image.SaveAsPng(temporaryPath);
            File.Move(temporaryPath, request.OutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static int CoordinateSeed(TileImageRequest request)
    {
        unchecked
        {
            var hash = (request.Seed * 397) ^ request.Coordinate.X;
            return ((hash * 397) ^ request.Coordinate.Y) & int.MaxValue;
        }
    }

    private static string BuildPrompt(TileImageRequest request)
    {
        static string TopKey(IReadOnlyDictionary<string, float> values)
            => values.Count == 0 ? "neutral" : values.OrderByDescending(pair => pair.Value).First().Key;

        var features = request.Features;
        var detail = request.HighDetail ? "high detail" : "medium detail";
        return $"top-down world tile, {features.Biome}, {features.Mood}, "
            + $"{TopKey(features.MaterialWeights)}, {TopKey(features.MotifWeights)}, "
            + $"{TopKey(features.PaletteWeights)} palette, {detail}, game art";
    }
}
