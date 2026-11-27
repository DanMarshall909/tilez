using System.Net;
using System.Text.Json;
using SkiaSharp;
using Tilez;

namespace Tilez.Tests;

public sealed class ComfyUiImageTests
{
    [Theory]
    [InlineData(true, "4:3")]
    [InlineData(false, "1:1")]
    public void Local_region_generation_returns_a_png_and_uses_a_reference_only_when_supplied(bool withReference, string aspectRatio)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Blue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        var handler = new RecordingComfyHandler(png);
        using var client = new HttpClient(handler);
        var generator = new ComfyUiTileImageGenerator(new ComfyUiImageOptions(
            new Uri("http://127.0.0.1:8188/"), "flux-2-klein-4b-fp8.safetensors",
            "qwen_3_4b.safetensors", "flux2-vae.safetensors"), client);

        var actual = generator.Generate(new RegionImageRequest("Extend the map", 42, aspectRatio,
            withReference ? png : null, withReference ? png : null, 192));

        Assert.Equal(png, actual);
        Assert.Equal(withReference ? 2 : 0, handler.UploadCount);
        using var prompt = JsonDocument.Parse(handler.PromptBody!);
        var nodes = prompt.RootElement.GetProperty("prompt").EnumerateObject().Select(p => p.Value).ToArray();
        Assert.Contains(nodes, n => n.GetProperty("class_type").GetString() == "Flux2Scheduler");
        Assert.Equal(withReference, nodes.Any(n => n.GetProperty("class_type").GetString() == "ReferenceLatent"));
        Assert.Equal(withReference, nodes.Any(n => n.GetProperty("class_type").GetString() == "LoadImage"));
        Assert.Equal(withReference, nodes.Any(n => n.GetProperty("class_type").GetString() == "LoadImageMask"));
        Assert.Equal(withReference, nodes.Any(n => n.GetProperty("class_type").GetString() == "VAEEncodeForInpaint"));
        if (!withReference)
        {
            var scheduler = nodes.Single(n => n.GetProperty("class_type").GetString() == "Flux2Scheduler");
            Assert.Equal(576, scheduler.GetProperty("inputs").GetProperty("width").GetInt32());
        }
        Assert.Contains("42", handler.PromptBody);
    }

    [Fact]
    public void Local_generation_failure_is_visible_and_does_not_return_a_placeholder()
    {
        var handler = new RecordingComfyHandler([], failPrompt: true);
        using var client = new HttpClient(handler);
        var generator = new ComfyUiTileImageGenerator(new ComfyUiImageOptions(
            new Uri("http://127.0.0.1:8188/"), "flux-2-klein-4b-fp8.safetensors",
            "qwen_3_4b.safetensors", "flux2-vae.safetensors"), client);

        var error = Assert.Throws<InvalidOperationException>(() =>
            generator.Generate(new RegionImageRequest("map", 1, "1:1", null)));

        Assert.Contains("ComfyUI", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zoom_frames_use_a_512_pixel_canvas_and_refine_the_previous_center_when_present(bool withReference)
    {
        using var bitmap = new SKBitmap(512, 512);
        bitmap.Erase(SKColors.Blue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        var handler = new RecordingComfyHandler(png);
        using var client = new HttpClient(handler);
        var generator = new ComfyUiTileImageGenerator(new ComfyUiImageOptions(
            new Uri("http://127.0.0.1:8188/"), "flux-2-klein-4b-fp8.safetensors",
            "qwen_3_4b.safetensors", "flux2-vae.safetensors"), client);

        generator.Generate(new ZoomFrameRequest("Zoom to the center", 42, withReference ? png : null)
        {
            MaskPng = withReference ? png : null
        });

        Assert.Equal(withReference ? 2 : 0, handler.UploadCount);
        using var prompt = JsonDocument.Parse(handler.PromptBody!);
        var nodes = prompt.RootElement.GetProperty("prompt").EnumerateObject()
            .ToDictionary(node => node.Name, node => node.Value);
        var scheduler = nodes.Values.Single(node => node.GetProperty("class_type").GetString() == "Flux2Scheduler");
        Assert.Equal(512, scheduler.GetProperty("inputs").GetProperty("width").GetInt32());
        Assert.Equal(512, scheduler.GetProperty("inputs").GetProperty("height").GetInt32());
        Assert.Equal(withReference ? 5 : 4, scheduler.GetProperty("inputs").GetProperty("steps").GetInt32());
        Assert.Equal(withReference, nodes.Values.Any(node => node.GetProperty("class_type").GetString() == "VAEEncode"));
        Assert.Equal(withReference, nodes.Values.Any(node => node.GetProperty("class_type").GetString() == "SplitSigmasDenoise"));
        Assert.Equal(withReference, nodes.Values.Any(node => node.GetProperty("class_type").GetString() == "VAEEncodeForInpaint"));
        var sampler = nodes.Values.Single(node => node.GetProperty("class_type").GetString() == "SamplerCustomAdvanced");
        var latentId = sampler.GetProperty("inputs").GetProperty("latent_image")[0].GetString()!;
        Assert.Equal(withReference ? "VAEEncodeForInpaint" : "EmptyFlux2LatentImage",
            nodes[latentId].GetProperty("class_type").GetString());
        if (withReference)
        {
            var split = nodes.Values.Single(node => node.GetProperty("class_type").GetString() == "SplitSigmasDenoise");
            Assert.Equal(1.0, split.GetProperty("inputs").GetProperty("denoise").GetDouble());
        }
    }

    private sealed class RecordingComfyHandler(byte[] png, bool failPrompt = false) : HttpMessageHandler
    {
        public int UploadCount { get; private set; }
        public string? PromptBody { get; private set; }
        private string _outputNode = "";

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/upload/image")
            {
                UploadCount++;
                return Json("{\"name\":\"tilez-reference.png\"}");
            }
            if (path == "/prompt")
            {
                PromptBody = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (failPrompt) return new HttpResponseMessage(HttpStatusCode.BadRequest);
                using var body = JsonDocument.Parse(PromptBody);
                _outputNode = body.RootElement.GetProperty("prompt").EnumerateObject()
                    .Single(p => p.Value.GetProperty("class_type").GetString() == "SaveImage").Name;
                return Json("{\"prompt_id\":\"test-id\"}");
            }
            if (path == "/history/test-id")
                return Json($"{{\"test-id\":{{\"outputs\":{{\"{_outputNode}\":{{\"images\":[{{\"filename\":\"tilez.png\",\"subfolder\":\"\",\"type\":\"output\"}}]}}}},\"status\":{{\"status_str\":\"success\"}}}}}}");
            if (path == "/view")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            throw new InvalidOperationException($"Unexpected endpoint: {path}");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
