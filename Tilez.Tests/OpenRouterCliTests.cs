using System.Diagnostics;
using Tilez;

namespace Tilez.Tests;

public sealed class OpenRouterCliTests
{
    [Fact]
    public void Missing_OpenRouter_key_fails_without_a_placeholder_or_a_logged_secret()
    {
        const string sentinel = "sentinel-openrouter-secret-do-not-log";
        var outputRoot = Path.Combine(Path.GetTempPath(), $"tilez-cli-{Guid.NewGuid():N}");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment.Remove("OPENROUTER_API_KEY");
        foreach (var argument in new[]
        {
            typeof(TileCoordinate).Assembly.Location,
            "render", "--image-backend", "openrouter", "--ai-api-key", sentinel,
            "--x", "0", "--y", "0", "--seed", "1337", "--output-root", outputRoot
        }) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "Tilez CLI did not exit within 30 seconds.");

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("OPENROUTER_API_KEY", stdout);
        Assert.DoesNotContain(sentinel, stdout + stderr);
        Assert.Empty(Directory.GetFiles(outputRoot, "*.png", SearchOption.AllDirectories));
        foreach (var log in Directory.GetFiles(outputRoot, "*.log", SearchOption.AllDirectories))
            Assert.DoesNotContain(sentinel, File.ReadAllText(log));
    }
}
