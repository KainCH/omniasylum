using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace OmniForge.Infrastructure.Services
{
    public class StreamMonitorService : IHostedService
    {
        private readonly IEventSubWebsocketClientWrapper _eventSubClient;
        private readonly TwitchAPI _twitchApi;
        private readonly ILogger<StreamMonitorService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TwitchSettings _twitchSettings;

        public StreamMonitorService(
            IEventSubWebsocketClientWrapper eventSubClient,
            TwitchAPI twitchApi,
            ILogger<StreamMonitorService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<TwitchSettings> twitchSettings)
        {
            _eventSubClient = eventSubClient;
            _twitchApi = twitchApi;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchSettings = twitchSettings.Value;

            _eventSubClient.WebsocketConnected += OnWebsocketConnected;
            _eventSubClient.WebsocketDisconnected += OnWebsocketDisconnected;
            _eventSubClient.WebsocketReconnected += OnWebsocketReconnected;
            _eventSubClient.ErrorOccurred += OnErrorOccurred;

            _eventSubClient.StreamOnline += OnStreamOnline;
            _eventSubClient.StreamOffline += OnStreamOffline;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting StreamMonitorService...");
            try
            {
                await _eventSubClient.ConnectAsync();
                _logger.LogInformation("StreamMonitorService connected to EventSub.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting StreamMonitorService.");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping StreamMonitorService...");
            try
            {
                await _eventSubClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping StreamMonitorService.");
            }
        }

        private async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
        {
            _logger.LogInformation($"Websocket connected. Session ID: {_eventSubClient.SessionId}");

            if (e.IsRequestedReconnect) return;

            // Subscribe to events for all users
            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var users = await userRepository.GetAllUsersAsync();

                foreach (var user in users)
                {
                    try
                    {
                        // Ensure TwitchAPI has credentials
                        if (string.IsNullOrEmpty(_twitchApi.Settings.AccessToken))
                        {
                            _twitchApi.Settings.ClientId = _twitchSettings.ClientId;
                            _twitchApi.Settings.Secret = _twitchSettings.ClientSecret;
                            var token = await _twitchApi.Auth.GetAccessTokenAsync();
                            _twitchApi.Settings.AccessToken = token;
                        }

                        var condition = new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", user.TwitchUserId }
                        };

                        // Subscribe to Stream Online
                        await helixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchApi.Settings.ClientId,
                            _twitchApi.Settings.AccessToken,
                            "stream.online", "1", condition, EventSubTransportMethod.Websocket,
                            _eventSubClient.SessionId);

                        // Subscribe to Stream Offline
                        await helixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchApi.Settings.ClientId,
                            _twitchApi.Settings.AccessToken,
                            "stream.offline", "1", condition, EventSubTransportMethod.Websocket,
                            _eventSubClient.SessionId);

                        _logger.LogInformation($"Subscribed to Stream Online/Offline for user: {user.DisplayName} ({user.TwitchUserId})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to subscribe to events for user {user.DisplayName}");
                    }
                }
            }
        }

        private async Task OnWebsocketDisconnected(object? sender, WebsocketDisconnectedArgs e)
        {
            _logger.LogWarning("Websocket disconnected");
            await Task.CompletedTask;
        }

        private async Task OnWebsocketReconnected(object? sender, WebsocketReconnectedArgs e)
        {
            _logger.LogInformation("Websocket reconnected");
            await Task.CompletedTask;
        }

        private async Task OnErrorOccurred(object? sender, ErrorOccuredArgs e)
        {
            _logger.LogError(e.Exception, $"Websocket error: {e.Message}");
            await Task.CompletedTask;
        }

        private async Task OnStreamOnline(object? sender, StreamOnlineArgs e)
        {
            dynamic dynamicArgs = e;
            string? broadcasterId = null;
            string? broadcasterName = null;
            object? eventData = null;

            try
            {
                if (IsPropertyExist(dynamicArgs, "Event"))
                {
                    eventData = dynamicArgs.Event;
                    broadcasterId = dynamicArgs.Event.BroadcasterUserId;
                    broadcasterName = dynamicArgs.Event.BroadcasterUserName;
                }
                else if (IsPropertyExist(dynamicArgs, "Notification"))
                {
                     eventData = dynamicArgs.Notification.Payload.Event;
                     broadcasterId = dynamicArgs.Notification.Payload.Event.BroadcasterUserId;
                     broadcasterName = dynamicArgs.Notification.Payload.Event.BroadcasterUserName;
                }
            }
            catch
            {
            }

            if (broadcasterId == null)
            {
                 _logger.LogWarning("Could not extract broadcaster ID from StreamOnlineArgs");
                 return;
            }

            _logger.LogInformation($"Stream Online: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();

                var userId = broadcasterId;
                var user = await userRepository.GetUserAsync(userId);

                if (user != null)
                {
                    // Update Counter
                    var counters = await counterRepository.GetCountersAsync(userId);
                    if (counters != null)
                    {
                        counters.StreamStarted = DateTimeOffset.UtcNow;
                        await counterRepository.SaveCountersAsync(counters);
                    }

                    // Send Discord Notification
                    if (eventData != null)
                    {
                        await discordService.SendNotificationAsync(user, "stream.online", eventData);
                    }
                }
            }
        }

        private async Task OnStreamOffline(object? sender, StreamOfflineArgs e)
        {
            dynamic dynamicArgs = e;
            string? broadcasterId = null;
            string? broadcasterName = null;

            try
            {
                if (IsPropertyExist(dynamicArgs, "Event"))
                {
                    broadcasterId = dynamicArgs.Event.BroadcasterUserId;
                    broadcasterName = dynamicArgs.Event.BroadcasterUserName;
                }
                else if (IsPropertyExist(dynamicArgs, "Notification"))
                {
                     broadcasterId = dynamicArgs.Notification.Payload.Event.BroadcasterUserId;
                     broadcasterName = dynamicArgs.Notification.Payload.Event.BroadcasterUserName;
                }
            }
            catch
            {
            }

            if (broadcasterId == null) return;

            _logger.LogInformation($"Stream Offline: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();

                var userId = broadcasterId;
                var user = await userRepository.GetUserAsync(userId);

                if (user != null)
                {
                    // Update Counter
                    var counters = await counterRepository.GetCountersAsync(userId);
                    if (counters != null)
                    {
                        counters.StreamStarted = null;
                        await counterRepository.SaveCountersAsync(counters);
                    }
                }
            }
        }

        private bool IsPropertyExist(dynamic settings, string name)
        {
            if (settings is System.Dynamic.ExpandoObject)
                return ((IDictionary<string, object>)settings).ContainsKey(name);

            return settings.GetType().GetProperty(name) != null;
        }
    }
}
