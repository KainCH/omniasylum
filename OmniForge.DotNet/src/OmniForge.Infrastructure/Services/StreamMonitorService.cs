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
using OmniForge.Infrastructure.Models.EventSub;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

namespace OmniForge.Infrastructure.Services
{
    public class StreamMonitorService : IHostedService, IStreamMonitorService
    {
        private readonly INativeEventSubService _eventSubService;
        private readonly TwitchAPI _twitchApi;
        private readonly ILogger<StreamMonitorService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TwitchSettings _twitchSettings;
        private Timer? _connectionWatchdog;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _subscribedUsers = new();

        public StreamMonitorService(
            INativeEventSubService eventSubService,
            TwitchAPI twitchApi,
            ILogger<StreamMonitorService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<TwitchSettings> twitchSettings)
        {
            _eventSubService = eventSubService;
            _twitchApi = twitchApi;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchSettings = twitchSettings.Value;

            _eventSubService.OnSessionWelcome += OnSessionWelcome;
            _eventSubService.OnNotification += OnNotification;
            _eventSubService.OnDisconnected += OnDisconnected;
        }

        public async Task<bool> SubscribeToUserAsync(string userId)
        {
            if (!_eventSubService.IsConnected) return false;

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var user = await userRepository.GetUserAsync(userId);

                if (user == null || string.IsNullOrEmpty(user.AccessToken)) return false;

                try
                {
                    var condition = new Dictionary<string, string> { { "broadcaster_user_id", user.TwitchUserId } };
                    var sessionId = _eventSubService.SessionId;

                    if (string.IsNullOrEmpty(sessionId)) return false;

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "stream.online", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "stream.offline", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    _subscribedUsers.TryAdd(userId, true);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe user {UserId}", userId);
                    return false;
                }
            }
        }

        public async Task UnsubscribeFromUserAsync(string userId)
        {
            // Note: Helix doesn't easily support "unsubscribe by user" without tracking subscription IDs.
            // For now, we'll just mark as unsubscribed in our local tracking.
            // In a real implementation, we should store subscription IDs returned by CreateEventSubSubscriptionAsync.
            _subscribedUsers.TryRemove(userId, out _);
            await Task.CompletedTask;
        }

        public async Task<bool> ForceReconnectUserAsync(string userId)
        {
            // Re-subscribe
            return await SubscribeToUserAsync(userId);
        }

        public StreamMonitorStatus GetUserConnectionStatus(string userId)
        {
            return new StreamMonitorStatus
            {
                Connected = _eventSubService.IsConnected,
                Subscriptions = _subscribedUsers.ContainsKey(userId) ? new[] { "stream.online", "stream.offline" } : Array.Empty<string>(),
                LastConnected = _eventSubService.IsConnected ? DateTimeOffset.UtcNow : null // Approximate
            };
        }

        public bool IsUserSubscribed(string userId)
        {
            return _subscribedUsers.ContainsKey(userId);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting StreamMonitorService...");
            try
            {
                await _eventSubService.ConnectAsync();

                // Start watchdog to ensure connection stays alive
                _connectionWatchdog = new Timer(CheckConnection, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting StreamMonitorService.");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping StreamMonitorService...");
            _connectionWatchdog?.Dispose();
            try
            {
                await _eventSubService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping StreamMonitorService.");
            }
        }

        private async Task OnSessionWelcome(string sessionId)
        {
            _logger.LogInformation($"EventSub Session Welcome. ID: {sessionId}");

            // Auto-subscription is disabled. Users must manually start monitoring.
            // This prevents unauthorized or unwanted subscriptions on startup.
            await Task.CompletedTask;
        }

        private async Task OnDisconnected()
        {
            _logger.LogWarning("EventSub Disconnected.");
            await Task.CompletedTask;
        }

        private async void CheckConnection(object? state)
        {
            if (!_eventSubService.IsConnected)
            {
                _logger.LogWarning("Watchdog detected disconnected state. Attempting to reconnect...");
                try
                {
                    await _eventSubService.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watchdog reconnection failed.");
                }
            }
            else
            {
                // Check keepalive
                var timeSinceLastKeepalive = DateTime.UtcNow - _eventSubService.LastKeepaliveTime;
                if (timeSinceLastKeepalive.TotalSeconds > 30) // Assuming default keepalive is ~10s
                {
                    _logger.LogWarning($"No keepalive received for {timeSinceLastKeepalive.TotalSeconds:F1}s. Reconnecting...");
                    try
                    {
                        await _eventSubService.DisconnectAsync();
                        await _eventSubService.ConnectAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Keepalive reconnection failed.");
                    }
                }
            }
        }

        private async Task OnNotification(EventSubMessage message)
        {
            try
            {
                var subscriptionType = message.Payload.Subscription?.Type;
                var eventData = message.Payload.Event;

                if (subscriptionType == "stream.online")
                {
                    await HandleStreamOnline(eventData);
                }
                else if (subscriptionType == "stream.offline")
                {
                    await HandleStreamOffline(eventData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification.");
            }
        }

        private async Task HandleStreamOnline(System.Text.Json.JsonElement eventData)
        {
            string? broadcasterId = null;
            string? broadcasterName = null;

            try
            {
                if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                    broadcasterId = idProp.GetString();

                if (eventData.TryGetProperty("broadcaster_user_name", out var nameProp))
                    broadcasterName = nameProp.GetString();
            }
            catch {}

            if (broadcasterId == null) return;

            _logger.LogInformation($"Stream Online: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>(); // Optional service

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

                    // Notify Overlay
                    if (overlayNotifier != null && counters != null)
                    {
                        await overlayNotifier.NotifyStreamStartedAsync(userId, counters);
                    }

                    // Fetch Stream Info
                    object notificationData = new { };
                    try
                    {
                        if (!string.IsNullOrEmpty(user.AccessToken) && !string.IsNullOrEmpty(_twitchSettings.ClientId))
                        {
                            var streams = await helixWrapper.GetStreamsAsync(_twitchSettings.ClientId, user.AccessToken, new List<string> { userId });
                            if (streams.Streams != null && streams.Streams.Length > 0)
                            {
                                var stream = streams.Streams[0];
                                notificationData = new
                                {
                                    title = stream.Title,
                                    game = stream.GameName,
                                    thumbnailUrl = stream.ThumbnailUrl.Replace("{width}", "640").Replace("{height}", "360") + $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                                    viewerCount = stream.ViewerCount,
                                    startedAt = stream.StartedAt,
                                    broadcasterName = stream.UserName
                                };
                                _logger.LogInformation($"Retrieved fresh stream info for {user.DisplayName}: {stream.Title} - {stream.GameName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to fetch stream info for {user.DisplayName}, using event data fallback.");
                    }

                    // Send Discord Notification
                    if (notificationData != null)
                    {
                        await discordService.SendNotificationAsync(user, "stream_start", notificationData);
                    }
                }
            }
        }

        private async Task HandleStreamOffline(System.Text.Json.JsonElement eventData)
        {
            string? broadcasterId = null;
            string? broadcasterName = null;

            try
            {
                if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                    broadcasterId = idProp.GetString();

                if (eventData.TryGetProperty("broadcaster_user_name", out var nameProp))
                    broadcasterName = nameProp.GetString();
            }
            catch {}

            if (broadcasterId == null) return;

            _logger.LogInformation($"Stream Offline: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

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

                    // Notify Overlay
                    if (overlayNotifier != null && counters != null)
                    {
                        await overlayNotifier.NotifyStreamEndedAsync(userId, counters);
                    }
                }
            }
        }
    }
}
