using System;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Repositories;
using OmniForge.Infrastructure.Services;

namespace OmniForge.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var storageAccountName = configuration["Azure:StorageAccountName"];

            if (!string.IsNullOrEmpty(storageAccountName))
            {
                // Use Managed Identity
                var serviceUrl = new Uri($"https://{storageAccountName}.table.core.windows.net");
                services.AddSingleton(new TableServiceClient(serviceUrl, new DefaultAzureCredential()));
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
            services.AddSingleton<ITwitchMessageHandler, TwitchMessageHandler>();
            services.AddSingleton<ITwitchClientManager, TwitchClientManager>();
            services.AddHostedService<TwitchConnectionService>();

            return services;
        }
    }
}
