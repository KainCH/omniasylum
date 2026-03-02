using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniForge.SceneSync.Configuration;
using OmniForge.SceneSync.Services;

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
builder.Services.Configure<SceneSyncSettings>(
    builder.Configuration.GetSection("SceneSync"));

// Register core services
builder.Services.AddSingleton<SceneSyncOrchestrator>();

// Register typed HttpClient for the API client
builder.Services.AddHttpClient<OmniForgeApiClient>();

// Register hosted services for OBS and Streamlabs
builder.Services.AddHostedService<ObsSceneService>();
builder.Services.AddHostedService<StreamlabsSceneService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OmniForge.SceneSync");
logger.LogInformation("🚀 OmniForge SceneSync starting...");
logger.LogInformation("   Press Ctrl+C to stop");

await host.RunAsync();
