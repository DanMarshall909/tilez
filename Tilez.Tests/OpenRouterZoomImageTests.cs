using System.Net;
using System.Text.Json;
using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class OpenRouterZoomImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenAi_model_on_OpenRouter_uses_a_native_1024_image_and_current_frame_reference(bool hasReference)
    {
        using var image = new SKBitmap(1024, 1024);
        image.Erase(SKColors.Teal);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        var handler = new RecordingHandler(png);
        using var client = new HttpClient(handler);
        var backend = new OpenRouterZoomFrameImageGenerator(new OpenRouterZoomImageOptions(
            "openai/gpt-image-2.6-sunburst", "sentinel-key", "medium"), client);

        var result = backend.Generate(new ZoomFrameRequest("Nested steampunk scene", 42,
            hasReference ? png : null, 1024) { MaskPng = hasReference ? png : null });

        Assert.Equal(png, result);
        Assert.Equal("https://openrouter.ai/api/v1/images", handler.Uri?.ToString());
        Assert.Equal("Bearer sentinel-key", handler.Authorization);
        using var payload = JsonDocument.Parse(handler.Body!);
        var root = payload.RootElement;
        Assert.Equal("openai/gpt-image-2.6-sunburst", root.GetProperty("model").GetString());
        Assert.Equal("1024x1024", root.GetProperty("size").GetString());
        Assert.Equal("medium", root.GetProperty("quality").GetString());
        Assert.Equal("png", root.GetProperty("output_format").GetString());
        Assert.Equal("Nested steampunk scene", root.GetProperty("prompt").GetString());
        Assert.Equal(hasReference, root.TryGetProperty("input_references", out var references));
        if (hasReference)
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png),
                references[0].GetProperty("image_url").GetProperty("url").GetString());
        Assert.False(root.TryGetProperty("mask", out _));
        Assert.False(root.TryGetProperty("seed", out _));
    }

    [Fact]
    public void OpenRouter_image_failure_remains_visible()
    {
        var handler = new RecordingHandler([], HttpStatusCode.PaymentRequired);
        using var client = new HttpClient(handler);
        var backend = new OpenRouterZoomFrameImageGenerator(new OpenRouterZoomImageOptions(
            "openai/gpt-image-2.6-sunburst", "sentinel-key", "medium"), client);

        var error = Assert.Throws<InvalidOperationException>(() =>
            backend.Generate(new ZoomFrameRequest("Nested scene", 1, null, 1024)));

        Assert.Contains("402", error.Message);
        Assert.DoesNotContain("sentinel-key", error.Message);
    }

    private sealed class RecordingHandler(byte[] png, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var reply = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } });
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
