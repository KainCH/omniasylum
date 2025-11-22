using OmniForge.Web.Components;
using OmniForge.Infrastructure;
using OmniForge.Web.Hubs;
using OmniForge.Web.Services;
using OmniForge.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.Twitch;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Key Vault
var keyVaultName = builder.Configuration["KeyVaultName"];
if (!string.IsNullOrEmpty(keyVaultName))
{
    var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
    builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
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
.AddCookie()
.AddTwitch(options =>
{
    options.ClientId = builder.Configuration["Authentication:Twitch:ClientId"] ?? builder.Configuration["TWITCH-CLIENT-ID"] ?? throw new InvalidOperationException("Twitch ClientId not found.");
    options.ClientSecret = builder.Configuration["Authentication:Twitch:ClientSecret"] ?? builder.Configuration["TWITCH-CLIENT-SECRET"] ?? throw new InvalidOperationException("Twitch ClientSecret not found.");
    options.CallbackPath = "/auth/twitch/callback";
    options.Scope.Add("user:read:email");
    options.Scope.Add("chat:read");
    options.Scope.Add("chat:edit");
    options.SaveTokens = true;
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<IOverlayNotifier, SignalROverlayNotifier>();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

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
