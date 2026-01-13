using System;
using System.Diagnostics.CodeAnalysis;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniForge.Core.Configuration;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Repositories;
using OmniForge.Infrastructure.Services;
using OmniForge.Infrastructure.Services.EventHandlers;
using TwitchLib.Api;
using TwitchLib.EventSub.Websockets.Extensions;

namespace OmniForge.Infrastructure
{
    [ExcludeFromCodeCoverage]
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TwitchSettings>(configuration.GetSection("Twitch"));
            services.Configure<DiscordBotSettings>(configuration.GetSection("DiscordBot"));
            services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
            services.Configure<AzureTableConfiguration>(configuration.GetSection(AzureTableConfiguration.SectionName));
            services.Configure<RedisSettings>(configuration.GetSection("Redis"));

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
            services.AddScoped<IBotCredentialRepository, BotCredentialRepository>();
            services.AddScoped<ICounterRepository, CounterRepository>();
            services.AddScoped<IAlertRepository, AlertRepository>();
            services.AddScoped<IChannelPointRepository, ChannelPointRepository>();
            services.AddScoped<ISeriesRepository, SeriesRepository>();
            services.AddScoped<IGameLibraryRepository, GameLibraryRepository>();
            services.AddScoped<IGameCountersRepository, GameCountersRepository>();
            services.AddScoped<IGameContextRepository, GameContextRepository>();
            services.AddScoped<ICounterLibraryRepository, CounterLibraryRepository>();
            services.AddScoped<ICounterRequestRepository, CounterRequestRepository>();
            services.AddScoped<IGameChatCommandsRepository, GameChatCommandsRepository>();
            services.AddScoped<IGameCustomCountersConfigRepository, GameCustomCountersConfigRepository>();
            services.AddScoped<IGameCoreCountersConfigRepository, GameCoreCountersConfigRepository>();
            services.AddScoped<IAlertEventRouter, AlertEventRouter>();
            services.AddScoped<ITwitchHelixWrapper, TwitchHelixWrapper>();
            services.AddScoped<ITwitchApiService, TwitchApiService>();

            services.AddScoped<IGameSwitchService, GameSwitchService>();
            services.AddScoped<IGameCounterSetupService, GameCounterSetupService>();
            services.AddScoped<CoreCounterLibrarySeeder>();
            // Bot eligibility caching: Redis when configured, otherwise in-memory.
            services.AddSingleton<IBotEligibilityCache>(sp =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisSettings>>().Value;
                if (!string.IsNullOrWhiteSpace(settings.HostName))
                {
                    return ActivatorUtilities.CreateInstance<RedisBotEligibilityCache>(sp);
                }

                return new MemoryBotEligibilityCache();
            });

            services.AddScoped<ITwitchBotEligibilityService, TwitchBotEligibilityService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddSingleton<IDiscordBotClient, DiscordNetBotClient>();
            services.AddHostedService<DiscordBotPresenceHostedService>();
            services.AddHttpClient<IDiscordService, DiscordService>();
            services.AddSingleton<IChatCommandProcessor, ChatCommandProcessor>();
            services.AddSingleton<ITwitchMessageHandler, TwitchMessageHandler>();
            services.AddSingleton<ITwitchClientManager, TwitchClientManager>();
            services.AddHostedService<TwitchConnectionService>();

            services.AddSingleton<IMonitoringRegistry, MonitoringRegistry>();

            // EventSub Event Handlers (Strategy Pattern)
            services.AddSingleton<IDiscordNotificationTracker, DiscordNotificationTracker>();
            services.AddScoped<IDiscordInviteBroadcastScheduler, DiscordInviteBroadcastScheduler>();
            services.AddScoped<IDiscordInviteSender, DiscordInviteSender>();
            services.AddScoped<IEventSubHandler, StreamOnlineHandler>();
            services.AddScoped<IEventSubHandler, StreamOfflineHandler>();
            services.AddScoped<IEventSubHandler, FollowHandler>();
            services.AddScoped<IEventSubHandler, SubscriptionGiftHandler>();
            services.AddScoped<IEventSubHandler, SubscriptionMessageHandler>();
            services.AddScoped<IEventSubHandler, CheerHandler>();
            services.AddScoped<IEventSubHandler, RaidHandler>();
            services.AddScoped<IEventSubHandler, ChatMessageHandler>();
            services.AddScoped<IEventSubHandler, ChatNotificationHandler>();
            services.AddScoped<IEventSubHandler, ChannelUpdateHandler>();
            services.AddScoped<IEventSubHandlerRegistry, EventSubHandlerRegistry>();

            // Twitch EventSub
            services.AddSingleton<IEventSubMessageProcessor, EventSubMessageProcessor>();
            services.AddSingleton<INativeEventSubService, NativeEventSubService>();
            services.AddSingleton<TwitchAPI>();
            services.AddSingleton<StreamMonitorService>();
            services.AddHostedService(sp => sp.GetRequiredService<StreamMonitorService>());
            services.AddSingleton<IStreamMonitorService>(sp => sp.GetRequiredService<StreamMonitorService>());

            return services;
        }
    }
}
