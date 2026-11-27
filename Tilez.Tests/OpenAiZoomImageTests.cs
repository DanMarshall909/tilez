using System.Net;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class OpenAiZoomImageTests
{
    [Theory]
    [InlineData(false, "/v1/images/generations")]
    [InlineData(true, "/v1/images/edits")]
    public void Zoom_uses_generation_then_a_transparent_center_mask_for_edits(bool hasReference, string path)
    {
        using var image = new SKBitmap(1024, 1024);
        image.Erase(SKColors.Teal);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        using var opaqueMask = new SKBitmap(1024, 1024);
        opaqueMask.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(opaqueMask))
        using (var paint = new SKPaint { Color = SKColors.White })
            canvas.DrawRect(new SKRect(256, 256, 768, 768), paint);
        using var encodedMask = opaqueMask.Encode(SKEncodedImageFormat.Png, 100);
        var handler = new RecordingHandler(png);
        using var client = new HttpClient(handler);
        var backend = new OpenAiZoomFrameImageGenerator(
            new OpenAiZoomImageOptions("gpt-image-2.5-sunburst", "sentinel-key", "medium"), client);

        var actual = backend.Generate(new ZoomFrameRequest("Steampunk details", 42, hasReference ? png : null, 1024)
        {
            MaskPng = hasReference ? encodedMask.ToArray() : null
        });

        Assert.Equal(png, actual);
        Assert.Equal(path, handler.Path);
        Assert.Equal("Bearer sentinel-key", handler.Authorization);
        Assert.Equal("gpt-image-2.5-sunburst", handler.Fields["model"]);
        Assert.Equal("Steampunk details", handler.Fields["prompt"]);
        Assert.Equal("1024x1024", handler.Fields["size"]);
        Assert.Equal("medium", handler.Fields["quality"]);
        Assert.Equal("png", handler.Fields["output_format"]);
        if (!hasReference)
            Assert.Equal(JsonValueKind.Number, handler.NKind);
        Assert.False(handler.Fields.ContainsKey("seed"));
        if (hasReference)
        {
            Assert.Equal(png, handler.Files["image"]);
            using var transparentMask = SKBitmap.Decode(handler.Files["mask"]);
            Assert.NotNull(transparentMask);
            Assert.Equal((1024, 1024), (transparentMask.Width, transparentMask.Height));
            Assert.Equal(255, transparentMask.GetPixel(0, 0).Alpha);
            Assert.Equal(0, transparentMask.GetPixel(512, 512).Alpha);
        }
        else
            Assert.Empty(handler.Files);
    }

    [Fact]
    public void Failed_image_edit_exposes_the_api_error_without_a_placeholder()
    {
        var handler = new RecordingHandler([], HttpStatusCode.TooManyRequests, "credit_balance_exhausted");
        using var client = new HttpClient(handler);
        var backend = new OpenAiZoomFrameImageGenerator(
            new OpenAiZoomImageOptions("gpt-image-2.5-sunburst", "sentinel-key", "medium"), client);

        var error = Assert.Throws<InvalidOperationException>(() =>
            backend.Generate(new ZoomFrameRequest("Steampunk details", 42, null, 1024)));

        Assert.Contains("429", error.Message);
        Assert.Contains("credit_balance_exhausted", error.Message);
        Assert.DoesNotContain("sentinel-key", error.Message);
    }

    private sealed class RecordingHandler(byte[] png, HttpStatusCode status = HttpStatusCode.OK, string? errorCode = null)
        : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public Dictionary<string, string> Fields { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = [];
        public JsonValueKind? NKind { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (part.Headers.ContentDisposition.FileName is null)
                        Fields[name] = part.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                    else
                        Files[name] = part.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
                }
            }
            else
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    Fields[property.Name] = property.Value.ToString();
                    if (property.Name == "n") NKind = property.Value.ValueKind;
                }
            }

            var reply = errorCode is null
                ? JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } })
                : JsonSerializer.Serialize(new { error = new { code = errorCode } });
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
