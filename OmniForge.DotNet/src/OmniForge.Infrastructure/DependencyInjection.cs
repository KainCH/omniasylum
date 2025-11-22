using System;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Repositories;
using OmniForge.Infrastructure.Services;
using TwitchLib.Api;
using TwitchLib.EventSub.Websockets.Extensions;

namespace OmniForge.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TwitchSettings>(configuration.GetSection("Twitch"));
            services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

            services.AddHttpClient<ITwitchAuthService, TwitchAuthService>();
            services.AddScoped<IJwtService, JwtService>();

            var storageAccountName = configuration["AzureStorage:AccountName"];
            var azureClientId = configuration["AZURE_CLIENT_ID"];

            if (!string.IsNullOrEmpty(storageAccountName))
            {
                // Use Managed Identity
                var serviceUrl = new Uri($"https://{storageAccountName}.table.core.windows.net");

                if (!string.IsNullOrEmpty(azureClientId))
                {
                    services.AddSingleton(new TableServiceClient(serviceUrl, new ManagedIdentityCredential(azureClientId)));
                }
                else
                {
                    services.AddSingleton(new TableServiceClient(serviceUrl, new DefaultAzureCredential()));
                }
            }
            else
            {
                // Fallback to development storage or connection string if provided
                var connectionString = configuration["Azure:StorageConnectionString"];
                if (!string.IsNullOrEmpty(connectionString))
                {
                    services.AddSingleton(new TableServiceClient(connectionString));
                }
                else
                {
                    // Local emulator or throw
                    // For now, let's assume local emulator if nothing is configured
                    services.AddSingleton(new TableServiceClient("UseDevelopmentStorage=true"));
                }
            }

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICounterRepository, CounterRepository>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddHttpClient<IDiscordService, DiscordService>();
            services.AddSingleton<ITwitchMessageHandler, TwitchMessageHandler>();
            services.AddSingleton<ITwitchClientManager, TwitchClientManager>();
            services.AddHostedService<TwitchConnectionService>();

            // Twitch EventSub
            services.AddTwitchLibEventSubWebsockets();
            services.AddSingleton<TwitchAPI>();
            services.AddHostedService<StreamMonitorService>();

            return services;
        }
    }
}
