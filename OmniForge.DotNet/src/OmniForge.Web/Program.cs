using OmniForge.Web.Components;
using OmniForge.Infrastructure;
using OmniForge.Web.Hubs;
using OmniForge.Web.Services;
using OmniForge.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using AspNet.Security.OAuth.Twitch;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

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
        builder.Configuration.AddAzureKeyVault(keyVaultUri, new ManagedIdentityCredential(azureClientId));
    }
    else
    {
        // Fallback to DefaultAzureCredential for local development
        builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
    }
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = TwitchAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
})
.AddTwitch(options =>
{
    var clientId = builder.Configuration["Authentication:Twitch:ClientId"];
    if (string.IsNullOrEmpty(clientId))
    {
        clientId = builder.Configuration["TWITCH-CLIENT-ID"];
    }

    var clientSecret = builder.Configuration["Authentication:Twitch:ClientSecret"];
    if (string.IsNullOrEmpty(clientSecret))
    {
        clientSecret = builder.Configuration["TWITCH-CLIENT-SECRET"];
    }

    options.ClientId = !string.IsNullOrEmpty(clientId) ? clientId : throw new InvalidOperationException("Twitch ClientId not found.");
    options.ClientSecret = !string.IsNullOrEmpty(clientSecret) ? clientSecret : throw new InvalidOperationException("Twitch ClientSecret not found.");

    options.CallbackPath = "/auth/twitch/callback";
    options.Scope.Add("user:read:email");
    options.Scope.Add("chat:read");
    options.Scope.Add("chat:edit");
    options.Scope.Add("channel:manage:broadcast");
    options.Scope.Add("user:manage:whispers");
    options.Scope.Add("channel:read:subscriptions");
    options.Scope.Add("channel:read:redemptions");
    options.Scope.Add("moderator:read:followers");
    options.Scope.Add("bits:read");
    options.Scope.Add("clips:edit");
    options.SaveTokens = true;
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<IOverlayNotifier, SignalROverlayNotifier>();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();
app.MapHub<OverlayHub>("/overlayHub");

app.Run();
