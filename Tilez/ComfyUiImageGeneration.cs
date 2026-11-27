using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using SkiaSharp;

namespace Tilez;

public sealed record ComfyUiImageOptions(Uri BaseUrl, string DiffusionModel, string TextEncoder, string Vae)
{
    public static ComfyUiImageOptions LocalDefault(Uri? baseUrl = null) => new(
        baseUrl ?? new Uri("http://127.0.0.1:8188/"),
        "flux-2-klein-4b-fp8.safetensors", "qwen_3_4b.safetensors", "flux2-vae.safetensors");
}

public sealed class ComfyUiTileImageGenerator : ITileImageGenerator, IRegionImageGenerator, IZoomFrameImageGenerator
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly ComfyUiImageOptions _options;
    private readonly HttpClient _client;

    public ComfyUiTileImageGenerator(ComfyUiImageOptions options, HttpClient? client = null)
    {
        if (options.BaseUrl.Scheme != Uri.UriSchemeHttp || !options.BaseUrl.IsLoopback)
            throw new ArgumentException("The ComfyUI endpoint must be local HTTP.", nameof(options));
        _options = options;
        _client = client ?? SharedClient;
    }

    public string BackendName => "local ComfyUI FLUX.2 Klein";

    public void Generate(TileImageRequest request)
    {
        var prompt = $"Top-down illustrated world map, {request.Features.Biome}, {request.Features.Mood}, " +
                     $"{string.Join(", ", request.Features.MotifWeights.Keys)}, " +
                     $"{string.Join(", ", request.Features.MaterialWeights.Keys)}, game art.";
        var bytes = Generate(new RegionImageRequest(prompt, request.Seed, "1:1", null));
        using var image = PngImages.Decode(bytes);
        using var resized = PngImages.Resize(image, request.Size, SKFilterMode.Linear);
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        PngImages.Save(resized, request.OutputPath);
    }

    public byte[] Generate(RegionImageRequest request)
    {
        var (width, height) = request.AspectRatio switch
        {
            "1:1" => (request.CellSize * 3, request.CellSize * 3),
            "4:3" => (request.CellSize * 4, request.CellSize * 3),
            "3:4" => (request.CellSize * 3, request.CellSize * 4),
            _ => throw new ArgumentException("Unsupported region aspect ratio.", nameof(request))
        };
        if (request.ReferencePng is not null)
        {
            if (request.MaskPng is null)
                throw new ArgumentException("Local outpainting requires a mask for the blank area.", nameof(request));
            using var reference = PngImages.Decode(request.ReferencePng);
            using var mask = PngImages.Decode(request.MaskPng);
            if (reference.Width != mask.Width || reference.Height != mask.Height)
                throw new InvalidDataException("The outpainting mask must match the reference image.");
            width = reference.Width;
            height = reference.Height;
        }
        string? referenceName = null;
        string? maskName = null;
        if (request.ReferencePng is not null)
        {
            referenceName = UploadPng(request.ReferencePng);
            maskName = UploadPng(request.MaskPng!);
        }

        var workflow = BuildWorkflow(request, width, height, referenceName, maskName);
        return RunWorkflow(workflow);
    }

    public byte[] Generate(ZoomFrameRequest request)
    {
        if (request.Size != ZoomFrameRenderer.RenderSize)
            throw new ArgumentOutOfRangeException(nameof(request), "Zoom frames must be 512 pixels square.");
        string? referenceName = null;
        if (request.ReferencePng is not null)
        {
            using var reference = PngImages.Decode(request.ReferencePng);
            if (reference.Width != request.Size || reference.Height != request.Size)
                throw new InvalidDataException("The zoom reference must be 512 pixels square.");
            referenceName = UploadPng(request.ReferencePng);
        }
        var workflow = BuildZoomWorkflow(request, referenceName);
        return RunWorkflow(workflow);
    }

    private byte[] RunWorkflow(Dictionary<string, object> workflow)
    {
        using var promptReply = Send(HttpMethod.Post, "prompt", JsonContent.Create(new
        {
            prompt = workflow,
            client_id = Guid.NewGuid().ToString("N")
        }));
        using var queued = JsonDocument.Parse(promptReply.Content.ReadAsStream());
        var promptId = queued.RootElement.GetProperty("prompt_id").GetString()
            ?? throw new InvalidDataException("ComfyUI did not return a prompt ID.");

        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromMinutes(20))
        {
            using var historyReply = Send(HttpMethod.Get, "history/" + Uri.EscapeDataString(promptId));
            using var history = JsonDocument.Parse(historyReply.Content.ReadAsStream());
            if (history.RootElement.TryGetProperty(promptId, out var item))
            {
                if (item.TryGetProperty("status", out var status)
                    && status.TryGetProperty("status_str", out var statusText)
                    && statusText.GetString() == "error")
                    throw new InvalidOperationException("ComfyUI image generation failed; inspect the local ComfyUI log.");
                if (item.TryGetProperty("outputs", out var outputs)
                    && outputs.TryGetProperty("13", out var save)
                    && save.TryGetProperty("images", out var images)
                    && images.GetArrayLength() > 0)
                {
                    var artifact = images[0];
                    var filename = artifact.GetProperty("filename").GetString()!;
                    var subfolder = artifact.GetProperty("subfolder").GetString() ?? "";
                    var type = artifact.GetProperty("type").GetString() ?? "output";
                    var view = "view?filename=" + Uri.EscapeDataString(filename)
                        + "&subfolder=" + Uri.EscapeDataString(subfolder)
                        + "&type=" + Uri.EscapeDataString(type);
                    using var imageReply = Send(HttpMethod.Get, view);
                    var bytes = imageReply.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    using var decoded = PngImages.Decode(bytes);
                    return bytes;
                }
            }
            Thread.Sleep(500);
        }
        throw new TimeoutException("ComfyUI did not finish image generation within 20 minutes.");
    }

    private HttpResponseMessage Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var message = new HttpRequestMessage(method, new Uri(_options.BaseUrl, path)) { Content = content };
        HttpResponseMessage reply;
        try
        {
            reply = _client.Send(message);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("ComfyUI is unavailable at " + _options.BaseUrl, error);
        }
        if (!reply.IsSuccessStatusCode)
        {
            var detail = reply.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            reply.Dispose();
            throw new InvalidOperationException($"ComfyUI {path} failed with HTTP {(int)reply.StatusCode}: {detail[..Math.Min(400, detail.Length)]}");
        }
        return reply;
    }

    private string UploadPng(byte[] png)
    {
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(png), "image", $"tilez-{Guid.NewGuid():N}.png");
        using var uploadReply = Send(HttpMethod.Post, "upload/image", upload);
        using var uploaded = JsonDocument.Parse(uploadReply.Content.ReadAsStream());
        return uploaded.RootElement.GetProperty("name").GetString()
            ?? throw new InvalidDataException("ComfyUI did not name the uploaded image.");
    }

    private Dictionary<string, object> BuildWorkflow(RegionImageRequest request, int width, int height,
        string? referenceName, string? maskName)
    {
        static object[] Ref(string id) => [id, 0];
        static object Node(string type, object inputs) => new { class_type = type, inputs };

        var nodes = new Dictionary<string, object>
        {
            ["1"] = Node("UNETLoader", new { unet_name = _options.DiffusionModel, weight_dtype = "default" }),
            ["2"] = Node("CLIPLoader", new { clip_name = _options.TextEncoder, type = "flux2", device = "default" }),
            ["3"] = Node("VAELoader", new { vae_name = _options.Vae }),
            ["4"] = Node("CLIPTextEncode", new { clip = Ref("2"), text = request.Prompt }),
            ["5"] = Node("ConditioningZeroOut", new { conditioning = Ref("4") }),
            ["6"] = Node("RandomNoise", new { noise_seed = request.Seed }),
            ["7"] = Node("KSamplerSelect", new { sampler_name = "euler" }),
            ["8"] = Node("Flux2Scheduler", new { steps = 4, width, height }),
            ["9"] = Node("EmptyFlux2LatentImage", new { width, height, batch_size = 1 }),
            ["11"] = Node("SamplerCustomAdvanced", new
            {
                noise = Ref("6"), guider = Ref("10"), sampler = Ref("7"),
                sigmas = Ref("8"), latent_image = Ref("9")
            }),
            ["12"] = Node("VAEDecode", new { samples = Ref("11"), vae = Ref("3") }),
            ["13"] = Node("SaveImage", new { images = Ref("12"), filename_prefix = "tilez-region" })
        };
        var positive = Ref("4");
        var negative = Ref("5");
        if (referenceName is not null)
        {
            nodes["14"] = Node("LoadImage", new { image = referenceName });
            nodes["15"] = Node("VAEEncode", new { pixels = Ref("14"), vae = Ref("3") });
            nodes["16"] = Node("ReferenceLatent", new { conditioning = Ref("4"), latent = Ref("15") });
            nodes["17"] = Node("ReferenceLatent", new { conditioning = Ref("5"), latent = Ref("15") });
            nodes["18"] = Node("LoadImageMask", new { image = maskName, channel = "red" });
            nodes["9"] = Node("VAEEncodeForInpaint", new
            {
                pixels = Ref("14"), vae = Ref("3"), mask = Ref("18"), grow_mask_by = 8
            });
            positive = Ref("16");
            negative = Ref("17");
        }
        nodes["10"] = Node("CFGGuider", new { model = Ref("1"), positive, negative, cfg = 1.0 });
        return nodes;
    }

    private Dictionary<string, object> BuildZoomWorkflow(ZoomFrameRequest request, string? referenceName)
    {
        static object[] Ref(string id, int output = 0) => [id, output];
        static object Node(string type, object inputs) => new { class_type = type, inputs };

        var nodes = BuildWorkflow(new RegionImageRequest(request.Prompt, request.Seed, "1:1", null),
            request.Size, request.Size, null, null);
        nodes["13"] = Node("SaveImage", new { images = Ref("12"), filename_prefix = "tilez-zoom" });
        if (referenceName is null) return nodes;

        nodes["14"] = Node("LoadImage", new { image = referenceName });
        nodes["15"] = Node("VAEEncode", new { pixels = Ref("14"), vae = Ref("3") });
        nodes["16"] = Node("ReferenceLatent", new { conditioning = Ref("4"), latent = Ref("15") });
        nodes["17"] = Node("ReferenceLatent", new { conditioning = Ref("5"), latent = Ref("15") });
        nodes["19"] = Node("SplitSigmasDenoise", new { sigmas = Ref("8"), denoise = 0.75 });
        nodes["10"] = Node("CFGGuider", new { model = Ref("1"), positive = Ref("16"), negative = Ref("17"), cfg = 1.0 });
        nodes["11"] = Node("SamplerCustomAdvanced", new
        {
            noise = Ref("6"), guider = Ref("10"), sampler = Ref("7"),
            sigmas = Ref("19", 1), latent_image = Ref("15")
        });
        return nodes;
    }
}
