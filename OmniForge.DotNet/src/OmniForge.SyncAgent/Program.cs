using OmniForge.SyncAgent;
using OmniForge.SyncAgent.Abstractions;
using OmniForge.SyncAgent.Services;
using Serilog;

// ── Install path ─────────────────────────────────────────────────────────────
// All agent operations (auto-start registry, auto-update) assume the exe lives
// at %AppData%\omni-forge\OmniForge.SyncAgent.exe.  On first launch from
// anywhere else (Downloads, Desktop, …) we copy there and relaunch.
var installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "omni-forge");
var installPath = Path.Combine(installDir, "OmniForge.SyncAgent.exe");

// Handle --update-from: we are the new version; wait for old process to release
// its exe lock, overwrite it with ourselves, then relaunch from that path.
if (args.Length >= 2 && args[0] == "--update-from")
{
    var oldPath = args[1];
    try
    {
        // Wait up to 5 s for old process to exit
        for (int i = 0; i < 50; i++)
        {
            try
            {
                using var fs = File.Open(oldPath, FileMode.Open, FileAccess.Write, FileShare.None);
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        // Copy ourselves onto the old path and relaunch from it
        var currentExe = Environment.ProcessPath!;
        File.Copy(currentExe, oldPath, overwrite: true);

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

// ── Self-install ──────────────────────────────────────────────────────────────
// If not running from the canonical install path, copy there and relaunch so
// that auto-start and updates always target the right location.
var currentExePath = Environment.ProcessPath ?? "";
if (!string.IsNullOrEmpty(currentExePath)
    && !string.Equals(Path.GetFullPath(currentExePath), Path.GetFullPath(installPath),
        StringComparison.OrdinalIgnoreCase))
{
    var selfInstalled = false;
    try
    {
        Directory.CreateDirectory(installDir);
        File.Copy(currentExePath, installPath, overwrite: true);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installPath)
        {
            UseShellExecute = true
        });
        selfInstalled = true;
        Console.WriteLine($"Installed to {installPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Self-install failed, running from current location: {ex.Message}");
    }

    if (selfInstalled) return;
}
// ─────────────────────────────────────────────────────────────────────────────

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "omni-forge", "logs", "sync-agent-.log");

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
