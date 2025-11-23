using OmniForge.Web.Components;
using OmniForge.Infrastructure;
using OmniForge.Web.Hubs;
using OmniForge.Web.Services;
using OmniForge.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using AspNet.Security.OAuth.Twitch;
using Azure.Identity;
using Azure.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Middleware;
using OmniForge.Web.Configuration;
using Microsoft.AspNetCore.Components.Server.Circuits;

var builder = WebApplication.CreateBuilder(args);

// Configure Forwarded Headers for Azure Container Apps
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configure Azure Key Vault
var keyVaultName = builder.Configuration["KeyVaultName"];
if (!string.IsNullOrEmpty(keyVaultName))
{
    var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
    var azureClientId = builder.Configuration["AZURE_CLIENT_ID"];

    if (!string.IsNullOrEmpty(azureClientId))
    {
        // Use Managed Identity Credential when deployed to Azure with User Assigned Identity
        builder.Configuration.AddAzureKeyVault(keyVaultUri, new ManagedIdentityCredential(azureClientId), new LegacyKeyVaultSecretManager());
    }
    else
    {
        // Fallback to DefaultAzureCredential for local development
        builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential(), new LegacyKeyVaultSecretManager());
    }
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddCircuitOptions(options => options.DetailedErrors = true); // Enable detailed errors for debugging

builder.Services.AddControllers();

// Configure Data Protection
var storageAccountName = builder.Configuration["AzureStorage:AccountName"];
if (!string.IsNullOrEmpty(storageAccountName))
{
    var blobUri = new Uri($"https://{storageAccountName}.blob.core.windows.net/dataprotection-keys/keys.xml");
    var azureClientId = builder.Configuration["AZURE_CLIENT_ID"];
    TokenCredential credential;

    if (!string.IsNullOrEmpty(azureClientId))
    {
        credential = new ManagedIdentityCredential(azureClientId);
    }
    else
    {
        credential = new DefaultAzureCredential();
    }

    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(blobUri, credential)
        .SetApplicationName("OmniForgeStream");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/auth/twitch";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
    if (jwtSettings == null) throw new InvalidOperationException("Jwt settings not found.");
    var key = Encoding.ASCII.GetBytes(jwtSettings.Secret);

    options.RequireHttpsMetadata = false; // Set to true in production
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 102400; // 100KB
    hubOptions.EnableDetailedErrors = true;
});
builder.Services.AddScoped<CircuitHandler, LoggingCircuitHandler>();

builder.Services.AddSingleton<IWebSocketOverlayManager, WebSocketOverlayManager>();
builder.Services.AddSingleton<IOverlayNotifier, WebSocketOverlayNotifier>();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Initialize Database Tables
using (var scope = app.Services.CreateScope())
{
    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
    var alertRepository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
    var channelPointRepository = scope.ServiceProvider.GetRequiredService<IChannelPointRepository>();
    var seriesRepository = scope.ServiceProvider.GetRequiredService<ISeriesRepository>();

    try
    {
        await userRepository.InitializeAsync();
        await counterRepository.InitializeAsync();
        await alertRepository.InitializeAsync();
        await channelPointRepository.InitializeAsync();
        await seriesRepository.InitializeAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing database: {ex.Message}");
    }
}

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles(); // Enable static file serving
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(); // Enable WebSockets
app.UseMiddleware<UserStatusMiddleware>();
app.UseMiddleware<WebSocketOverlayMiddleware>();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();
// app.MapHub<OverlayHub>("/overlayHub"); // Removed in favor of WebSockets

app.Run();
