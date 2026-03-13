using Tilez;
using System.Numerics;
using Serilog;

var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "generated");
Directory.CreateDirectory(outputRoot);

var logger = LoggingSetup.ConfigureLogging(outputRoot);
logger.Information("Starting Tilez CLI");
logger.Information("Output root: {OutputRoot}", outputRoot);
logger.Information("Arguments: {Args}", string.Join(" ", args));

try
{
    var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "single";
    logger.Information("Mode: {Mode}", mode);

    var imageGenerator = BuildImageGenerator(args);
    var generator = new SingleTileGenerator(imageGenerator);
    var painter = new WorldPainter(generator);
    var panEngine = new CameraPanEngine(generator);
    var promptSampler = new DistanceWeightedPromptFieldSampler();
    var zoomSampler = new ZoomPromptSamplingService(painter, promptSampler);
    var scalingPipeline = new DisplayScalingPipeline(generator, new CpuNearestNeighborUpscaler());

    switch (mode)
{
    case "paint":
        {
            var minX = GetInt(args, "--min-x", 0);
            var maxX = GetInt(args, "--max-x", 2);
            var minY = GetInt(args, "--min-y", 0);
            var maxY = GetInt(args, "--max-y", 2);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Paint mode: region [{MinX},{MinY}] to [{MaxX},{MaxY}], seed {Seed}",
                minX, minY, maxX, maxY, seed);
            var tiles = painter.PaintRectangle(minX, maxX, minY, maxY, seed, outputRoot);
            logger.Information("Painted {TileCount} tiles", tiles.Count);
            Console.WriteLine($"Painted {tiles.Count} tiles.");
            break;
        }

    case "author":
        {
            var filePath = GetString(args, "--file");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                logger.Error("Author mode: missing --file argument");
                throw new ArgumentException("Missing required --file path for author mode.");
            }

            logger.Information("Author mode: file {FilePath}", filePath);
            var tiles = painter.PaintFromAuthoringFile(filePath, outputRoot);
            logger.Information("Authored {TileCount} tiles from {FilePath}", tiles.Count, filePath);
            Console.WriteLine($"Authored {tiles.Count} tiles from {filePath}.");
            break;
        }

    case "prefetch":
        {
            var viewportMinX = GetInt(args, "--viewport-min-x", 0);
            var viewportMaxX = GetInt(args, "--viewport-max-x", 2);
            var viewportMinY = GetInt(args, "--viewport-min-y", 0);
            var viewportMaxY = GetInt(args, "--viewport-max-y", 2);
            var margin = GetInt(args, "--margin", 1);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Prefetch mode: viewport [{VpMinX},{VpMinY}] to [{VpMaxX},{VpMaxY}], margin {Margin}, seed {Seed}",
                viewportMinX, viewportMinY, viewportMaxX, viewportMaxY, margin, seed);

            var tiles = painter.PaintViewportWithMargin(
                viewportMinX,
                viewportMaxX,
                viewportMinY,
                viewportMaxY,
                margin,
                seed,
                outputRoot);

            logger.Information("Prefetched {TileCount} tiles", tiles.Count);
            Console.WriteLine(
                $"Prefetched {tiles.Count} tiles for viewport " +
                $"[{viewportMinX},{viewportMinY}] to [{viewportMaxX},{viewportMaxY}] with margin {margin}.");
            break;
        }

    case "sample":
        {
            var sampleX = GetFloat(args, "--x", 0.5f);
            var sampleY = GetFloat(args, "--y", 0.5f);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Sample mode: position ({SampleX:0.00}, {SampleY:0.00}), seed {Seed}",
                sampleX, sampleY, seed);

            var nearbyTiles = painter.PaintRectangle(-1, 1, -1, 1, seed, outputRoot);
            var blended = promptSampler.SampleAt(new Vector2(sampleX, sampleY), nearbyTiles);
            var prompt = PromptComposer.Compose(blended);

            logger.Information("Sample result: biome {Biome}, seam {Seam} ({Strength:0.00})",
                blended.DominantBiome, blended.SeamBehavior, blended.SeamInteractionStrength);
            logger.Debug("Generated prompt: {Prompt}", prompt);

            Console.WriteLine($"Sample position: ({sampleX:0.00}, {sampleY:0.00})");
            Console.WriteLine($"Dominant biome: {blended.DominantBiome}");
            Console.WriteLine($"Seam: {blended.SeamBehavior} ({blended.SeamInteractionStrength:0.00})");
            Console.WriteLine($"Prompt: {prompt}");
            break;
        }

    case "zoom-sample":
        {
            var sampleX = GetFloat(args, "--x", 0.5f);
            var sampleY = GetFloat(args, "--y", 0.5f);
            var zoom = GetFloat(args, "--zoom", 1.0f);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Zoom-sample mode: position ({SampleX:0.00}, {SampleY:0.00}), zoom {Zoom:0.00}, seed {Seed}",
                sampleX, sampleY, zoom, seed);

            var blended = zoomSampler.SampleAtZoom(new Vector2(sampleX, sampleY), zoom, seed, outputRoot);
            var prompt = PromptComposer.Compose(blended);

            logger.Information("Zoom-sample result: biome {Biome}, seam {Seam} ({Strength:0.00})",
                blended.DominantBiome, blended.SeamBehavior, blended.SeamInteractionStrength);
            logger.Debug("Generated prompt: {Prompt}", prompt);

            Console.WriteLine($"Zoom level: {zoom:0.00}");
            Console.WriteLine($"Dominant biome: {blended.DominantBiome}");
            Console.WriteLine($"Seam: {blended.SeamBehavior} ({blended.SeamInteractionStrength:0.00})");
            Console.WriteLine($"Prompt: {prompt}");
            break;
        }

    case "pan":
        {
            var startX = GetFloat(args, "--start-x", 0f);
            var startY = GetFloat(args, "--start-y", 0f);
            var endX = GetFloat(args, "--end-x", 5f);
            var endY = GetFloat(args, "--end-y", 0f);
            var viewportWidth = GetInt(args, "--viewport-width", 3);
            var viewportHeight = GetInt(args, "--viewport-height", 3);
            var steps = GetInt(args, "--steps", 10);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Pan mode: ({StartX:0.00}, {StartY:0.00}) to ({EndX:0.00}, {EndY:0.00}), viewport {VpWidth}x{VpHeight}, {Steps} steps, seed {Seed}",
                startX, startY, endX, endY, viewportWidth, viewportHeight, steps, seed);

            var generated = panEngine.PanAndGenerate(
                startX,
                startY,
                endX,
                endY,
                viewportWidth,
                viewportHeight,
                steps,
                seed,
                outputRoot);

            logger.Information("Pan completed: generated {GeneratedTiles} tiles", generated);
            Console.WriteLine($"Camera pan generated {generated} visible-priority tiles.");
            break;
        }

    case "render":
        {
            var x = GetInt(args, "--x", 0);
            var y = GetInt(args, "--y", 0);
            var zoom = GetFloat(args, "--zoom", 1.0f);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Render mode: tile ({X}, {Y}), zoom {Zoom:0.00}, seed {Seed}",
                x, y, zoom, seed);

            var tile = generator.Generate(new TileCoordinate(x, y), seed, outputRoot);
            var artifacts = scalingPipeline.RenderForDisplay(tile, zoom, outputRoot);

            logger.Information("Render completed: {ImageBackend} -> {RenderBackend}", imageGenerator.BackendName, artifacts.UpscalerBackend);
            logger.Debug("Artifacts: low-res {LowRes}, upscaled {Upscaled}, high-detail {HighDetail}, metadata {Metadata}",
                artifacts.LowResImagePath, artifacts.UpscaledImagePath, artifacts.HighDetailImagePath ?? "none", artifacts.MetadataPath);

            Console.WriteLine($"Image backend: {imageGenerator.BackendName}");
            Console.WriteLine($"Render backend: {artifacts.UpscalerBackend}");
            Console.WriteLine($"Low-res: {artifacts.LowResImagePath}");
            Console.WriteLine($"Upscaled: {artifacts.UpscaledImagePath}");
            Console.WriteLine($"High-detail: {artifacts.HighDetailImagePath ?? "not requested"}");
            Console.WriteLine($"Metadata: {artifacts.MetadataPath}");
            break;
        }

    default:
        {
            var x = GetInt(args, "--x", 0);
            var y = GetInt(args, "--y", 0);
            var seed = GetInt(args, "--seed", 1337);

            logger.Information("Single tile mode (default): tile ({X}, {Y}), seed {Seed}", x, y, seed);

            var tile = generator.Generate(new TileCoordinate(x, y), seed, outputRoot);
            var metadataPath = TilePersistence.SaveMetadata(tile, outputRoot);

            logger.Information("Tile generated: ({X}, {Y})", tile.Coordinate.X, tile.Coordinate.Y);
            logger.Debug("Summary: {Summary}", tile.TileSummary);
            logger.Debug("Image: {ImagePath}, Metadata: {MetadataPath}", tile.ImagePath, metadataPath);

            Console.WriteLine($"Generated tile: {tile.Coordinate.X},{tile.Coordinate.Y}");
            Console.WriteLine($"Summary: {tile.TileSummary}");
            Console.WriteLine($"Image: {tile.ImagePath}");
            Console.WriteLine($"Metadata: {metadataPath}");
            break;
        }
    }

    logger.Information("Tilez CLI completed successfully");
}
catch (Exception ex)
{
    logger.Fatal(ex, "Tilez CLI terminated with fatal error");
    Console.WriteLine($"FATAL ERROR: {ex.Message}");
    Environment.Exit(1);
}
finally
{
    LoggingSetup.CloseLogging();
}

static int GetInt(string[] args, string key, int defaultValue)
{
    var text = GetString(args, key);
    return int.TryParse(text, out var parsed) ? parsed : defaultValue;
}

static float GetFloat(string[] args, string key, float defaultValue)
{
    var text = GetString(args, key);
    return float.TryParse(text, out var parsed) ? parsed : defaultValue;
}

static string? GetString(string[] args, string key)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static ITileImageGenerator BuildImageGenerator(string[] args)
{
    var backend = GetString(args, "--image-backend")?.ToLowerInvariant()
        ?? Environment.GetEnvironmentVariable("TILEZ_IMAGE_BACKEND")?.ToLowerInvariant()
        ?? "procedural";

    Log.Information("Building image generator: backend={Backend}", backend);

    if (backend is "openai" or "ai")
    {
        var baseUrl = GetString(args, "--ai-base-url")
            ?? Environment.GetEnvironmentVariable("TILEZ_AI_BASE_URL")
            ?? "http://localhost:1234";

        var model = GetString(args, "--ai-model")
            ?? Environment.GetEnvironmentVariable("TILEZ_AI_MODEL")
            ?? "flux";

        var apiKey = GetString(args, "--ai-api-key")
            ?? Environment.GetEnvironmentVariable("TILEZ_AI_API_KEY");

        Log.Information("OpenAI-compatible backend: baseUrl={BaseUrl}, model={Model}",
            baseUrl, model);

        return new OpenAiCompatibleTileImageGenerator(new OpenAiImageOptions(baseUrl, model, apiKey));
    }

    Log.Information("Using procedural image generator");
    return new ProceduralTileImageGenerator();
}
