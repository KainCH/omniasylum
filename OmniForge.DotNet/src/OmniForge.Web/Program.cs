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
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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

builder.Services.AddSignalR();
builder.Services.AddSingleton<IOverlayNotifier, SignalROverlayNotifier>();
builder.Services.AddSingleton<IHubConnectionFactory, HubConnectionFactory>();

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
app.UseMiddleware<UserStatusMiddleware>();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();
app.MapHub<OverlayHub>("/overlayHub");

app.Run();
