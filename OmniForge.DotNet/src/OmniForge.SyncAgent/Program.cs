using OmniForge.SyncAgent;
using OmniForge.SyncAgent.Abstractions;
using OmniForge.SyncAgent.Services;
using Serilog;

// Handle --update-from for self-update
if (args.Length >= 2 && args[0] == "--update-from")
{
    var oldPath = args[1];
    try
    {
        // Wait for old process to exit
        for (int i = 0; i < 50; i++)
        {
            try
            {
                using var fs = File.Open(oldPath, FileMode.Open, FileAccess.Write, FileShare.None);
                break; // File is not locked, old process exited
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        // Copy self over the old path
        var currentExe = Environment.ProcessPath!;
        File.Copy(currentExe, oldPath, overwrite: true);

        // Relaunch from the original path
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(oldPath)
        {
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Update failed: {ex.Message}");
    }
    return;
}

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "OmniForge", "logs", "sync-agent-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("OmniForge Sync Agent starting...");

    // Load config store early
    var configStore = new AgentConfigStore();
    configStore.Load();

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .UseWindowsService()
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;
            var softwareMode = config.GetValue("StreamingSoftware", "auto")?.ToLowerInvariant() ?? "auto";

            // Config store and new services
            services.AddSingleton(configStore);
            services.AddSingleton<PairingService>();
            services.AddSingleton<AutoStartService>();
            services.AddSingleton<AutoUpdateService>();

            // Streaming software detection & client creation
            services.AddSingleton(new StreamingSoftwareDetectorOptions
            {
                PreferredMode = softwareMode,
                ObsPort = config.GetValue("Obs:Port", 4455)
            });
            services.AddSingleton<StreamingSoftwareDetector>();

            // Hosted services
            services.AddSingleton<StreamingSoftwareMonitor>();
            services.AddHostedService(sp => sp.GetRequiredService<StreamingSoftwareMonitor>());

            services.AddSingleton<ServerConnectionService>();
            services.AddHostedService(sp => sp.GetRequiredService<ServerConnectionService>());

            services.AddHostedService(sp => sp.GetRequiredService<AutoUpdateService>());

            services.AddSingleton<TrayIconService>();
            services.AddHostedService(sp => sp.GetRequiredService<TrayIconService>());
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sync Agent terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
