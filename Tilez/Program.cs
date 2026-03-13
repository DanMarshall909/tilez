using Tilez;

var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "generated");
Directory.CreateDirectory(outputRoot);

var coordinate = new TileCoordinate(0, 0);
var seed = 1337;

var generator = new SingleTileGenerator();
var tile = generator.Generate(coordinate, seed, outputRoot);

var metadataPath = TilePersistence.SaveMetadata(tile, outputRoot);

Console.WriteLine($"Generated tile: {tile.Coordinate.X},{tile.Coordinate.Y}");
Console.WriteLine($"Summary: {tile.TileSummary}");
Console.WriteLine($"Image: {tile.ImagePath}");
Console.WriteLine($"Metadata: {metadataPath}");
