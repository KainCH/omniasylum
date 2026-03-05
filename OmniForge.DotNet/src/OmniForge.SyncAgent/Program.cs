using OmniForge.SyncAgent.Abstractions;
using OmniForge.SyncAgent.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/sync-agent-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("OmniForge Sync Agent starting...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .UseWindowsService()
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;
            var softwareMode = config.GetValue("StreamingSoftware", "auto")?.ToLowerInvariant() ?? "auto";

            // Register streaming software client based on config
            if (softwareMode == "streamlabs")
            {
                services.AddSingleton<IStreamingSoftwareClient, StreamlabsDesktopClient>();
            }
            else if (softwareMode == "obs")
            {
                services.AddSingleton<IStreamingSoftwareClient, ObsWebSocketClient>();
            }
            else
            {
                // Auto-detect: try OBS first (most common), fall back to Streamlabs
                services.AddSingleton<IStreamingSoftwareClient>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<ObsWebSocketClient>>();
                    var slLogger = sp.GetRequiredService<ILogger<StreamlabsDesktopClient>>();

                    // Check if OBS WebSocket is likely available
                    try
                    {
                        using var tcpClient = new System.Net.Sockets.TcpClient();
                        var port = config.GetValue("Obs:Port", 4455);
                        tcpClient.Connect("127.0.0.1", port);
                        Log.Information("Auto-detected OBS WebSocket on port {Port}", port);
                        return new ObsWebSocketClient(config, logger);
                    }
                    catch
                    {
                        // OBS not available, check Streamlabs pipe
                        try
                        {
                            if (System.IO.File.Exists(@"\\.\pipe\slobs"))
                            {
                                Log.Information("Auto-detected Streamlabs Desktop named pipe");
                                return new StreamlabsDesktopClient(config, slLogger);
                            }
                        }
                        catch { }
                    }

                    // Default to OBS (will reconnect when available)
                    Log.Information("No streaming software detected, defaulting to OBS (will retry connection)");
                    return new ObsWebSocketClient(config, logger);
                });
            }

            // Hosted services
            services.AddSingleton<StreamingSoftwareMonitor>();
            services.AddHostedService(sp => sp.GetRequiredService<StreamingSoftwareMonitor>());

            services.AddSingleton<ServerConnectionService>();
            services.AddHostedService(sp => sp.GetRequiredService<ServerConnectionService>());

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
