using Serilog;
using Serilog.Events;
using System;

namespace Tilez;

public static class LoggingSetup
{
    public static ILogger ConfigureLogging(string outputRoot)
    {
        var logsDir = Path.Combine(outputRoot, "logs");
        Directory.CreateDirectory(logsDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        var logFilePath = Path.Combine(logsDir, $"tilez-{timestamp}.log");

        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                buffered: false)
            .CreateLogger();

        Log.Logger = logger;
        logger.Information("Tilez logging initialized. Logs: {LogPath}", logsDir);

        return logger;
    }

    public static void CloseLogging()
    {
        Log.CloseAndFlush();
    }
}
