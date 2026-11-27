using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class OpenRouterImageTests
{
    [Fact]
    public void Generates_a_png_through_the_OpenRouter_images_endpoint()
    {
        using var source = new SKBitmap(2, 2);
        using var stream = new MemoryStream();
        Assert.True(source.Encode(stream, SKEncodedImageFormat.Png, 100));
        var reply = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(stream.ToArray()) } } });
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(reply)
        });
        using var client = new HttpClient(handler);
        var path = Path.Combine(Path.GetTempPath(), $"tilez-{Guid.NewGuid():N}.png");
        var generator = new OpenRouterTileImageGenerator(
            new OpenRouterImageOptions("black-forest-labs/flux.2-klein-4b", "sentinel-key"), client);

        generator.Generate(Request(path));

        Assert.Equal("https://openrouter.ai/api/v1/images", handler.Uri?.ToString());
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("sentinel-key", handler.Authorization?.Parameter);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("black-forest-labs/flux.2-klein-4b", body.RootElement.GetProperty("model").GetString());
        Assert.Contains("top-down world tile", body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("1:1", body.RootElement.GetProperty("aspect_ratio").GetString());
        Assert.Equal("png", body.RootElement.GetProperty("output_format").GetString());
        Assert.True(body.RootElement.TryGetProperty("seed", out _));
        using var written = SKBitmap.Decode(path);
        Assert.NotNull(written);
        Assert.Equal(64, written.Width);
        Assert.Equal(64, written.Height);
    }

    [Fact]
    public void Api_failure_does_not_create_a_procedural_placeholder()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(handler);
        var path = Path.Combine(Path.GetTempPath(), $"tilez-{Guid.NewGuid():N}.png");
        var generator = new OpenRouterTileImageGenerator(
            new OpenRouterImageOptions("black-forest-labs/flux.2-klein-4b", "sentinel-key"), client);

        var error = Assert.Throws<InvalidOperationException>(() => generator.Generate(Request(path)));

        Assert.Contains("401", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Region_expansion_sends_the_spatial_reference_to_the_image_endpoint()
    {
        using var image = new SKBitmap(2, 2);
        using var stream = new MemoryStream();
        Assert.True(image.Encode(stream, SKEncodedImageFormat.Png, 100));
        var png = stream.ToArray();
        var reply = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } });
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(reply)
        });
        using var client = new HttpClient(handler);
        var generator = new OpenRouterTileImageGenerator(new OpenRouterImageOptions("black-forest-labs/flux.2-klein-4b", "sentinel-key"), client);

        var actual = generator.Generate(new RegionImageRequest("Extend the map", 42, "4:3", png));

        Assert.Equal(png, actual);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("4:3", body.RootElement.GetProperty("aspect_ratio").GetString());
        var url = body.RootElement.GetProperty("input_references")[0].GetProperty("image_url").GetProperty("url").GetString();
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), url);
    }

    private static TileImageRequest Request(string path) => new(
        new TileCoordinate(3, -2), 1337,
        new PromptFeatures("mechanical garden", "curious",
            new Dictionary<string, float> { ["roots"] = 1 },
            new Dictionary<string, float> { ["brass"] = 1 },
            new Dictionary<string, float> { ["amber"] = 1 }),
        64, false, "base", path);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
