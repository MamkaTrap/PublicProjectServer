using Serilog;

namespace SHINE.Server;

public static class Logger
{
    public static ILogger Log { get; }

    static Logger()
    {
        Directory.CreateDirectory("logs");

        Log = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/server-.log",
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_000_000,     
                rollOnFileSizeLimit: true)
            .CreateLogger();

        Log.Information("Logger ready.");
    }
}

